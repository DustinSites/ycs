﻿
/*
 * This source code is provided "as is" and without warranties as
 * to fitness for a particular purpose or merchantability. You may
 * use, distribute and modify this code under the terms of the
 * Microsoft Public License (Ms-PL) and you must retain all copyright,
 * patent, trademark, and attribution notices that are present in
 * the software.
 * 
 * You should have received a copy of the Microsoft Public License with
 * this file. If not, please write to: wickedcoder@hotmail.com,
 * or visit : http://www.microsoft.com/opensource/licenses.mspx#Ms-PL
 * Copyright (C) 2010 Wickedcoder - All Rights Reserved.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using System.Net;
using System.IO;
using System.Web;
using System.Threading;
using System.Text.RegularExpressions;
using System.Xml;
using System.Collections.Specialized;

namespace YCSLib
{
    public partial class YMSGConnection
    {
        public YMSGConnection()
        {
        }

        public YMSGConnection(string chatHost, int port)
        {
            this.Host = chatHost;
            this.Port = port;
        }

        #region events
        public Action<YMSGPacket> MessageReceived;
        public Action<YMSGNotifyEventArgs> NotifyInformation;
        #endregion

        #region properties
        public string CookieY { get; set; }
        public string CookieT { get; set; }
        public string Host { get; set; }
        public int Port { get; set; }
        public string LobbyName { get; protected set; }
        public int SessionID { get; protected set; }
        public string LoginName { get; protected set; }

        private Socket socket;
        private YMSGPacketBuilder pb = new YMSGPacketBuilder();
        ManualResetEvent isConnecting = new ManualResetEvent(true);
        AutoResetEvent isSending = new AutoResetEvent(true);
        #endregion

        #region connection intrinsics/async callbacks
        void ConnectCallback(IAsyncResult iar)
        {
            Socket s = ((YMSGConnection)iar.AsyncState).socket;
            s.EndConnect(iar);

            if (s.Connected)
            {
                ReceiveStore rs = new ReceiveStore(s)
                {
                    buffer = new byte[s.ReceiveBufferSize]
                };

                s.BeginReceive(rs.buffer, 0,
                    rs.buffer.Length, SocketFlags.None,
                    new AsyncCallback(ReceiveCallback), rs);
            }

            isConnecting.Set();
        }

        void ReceiveCallback(IAsyncResult iar)
        {
            ReceiveStore store = ((ReceiveStore)iar.AsyncState);
            if (store.socket != null)
            {
                int bytesRead = store.socket.EndReceive(iar);

                this.OnNotifyInformation(YMSGNotifyEventTypes.BytesReceived, bytesRead);

                if (bytesRead > 0)
                {
                    pb.AddBytes(store.buffer, bytesRead);
                    if (pb.Ready)
                    {
                        ThreadPool.QueueUserWorkItem(new WaitCallback(delegate(object raw)
                        {
                            byte[][] packets = raw as byte[][];
                            foreach (byte[] pd in packets)
                            {
                                YMSGPacket incoming = pd;
                                OnMessageReceived(incoming); // TODO: thread safe?
                            }
                        }), pb.GetPackets());
                    }
                }

                socket.BeginReceive(store.buffer, 0,
                    store.buffer.Length, SocketFlags.None,
                    new AsyncCallback(ReceiveCallback), store);
            }
        }
        #endregion

        #region methods
        /// <summary>
        /// These method facilititates cookie caching.
        /// </summary>
        /// <param name="username"></param>
        /// <param name="password"></param>
        public void RetrieveCookies(string username, string password)
        {
            string[] cookies = __SplitCookie(__GetCookie(username, password));
            this.CookieY = cookies[0];
            this.CookieT = cookies[1];
            this.LoginName = username;
        }

        /// <summary>
        /// These method facilititates cookie cache cleanup.
        /// </summary>
        public void ResetCookies()
        {
            this.CookieT = string.Empty; this.CookieY = string.Empty;
        }

        public virtual void Connect(string host = null, int? port = null)
        {
            if (!string.IsNullOrEmpty(host))
                this.Host = host;
            if (port.HasValue)
                this.Port = port.Value;

            if (socket == null)
            {
                socket = new Socket(AddressFamily.InterNetwork,
                    SocketType.Stream, ProtocolType.Tcp | ProtocolType.IP);
            }
            else if (socket.Connected)
                throw new InvalidOperationException(string.Format(Resources._1005, this.Host, this.Port));

            isConnecting.Reset();
            socket.BeginConnect(this.Host, this.Port,
                new AsyncCallback(ConnectCallback), this);
        }

        public virtual void Send(YMSGPacket packet)
        {
            packet.SessionID = (this.SessionID != 0 ? this.SessionID : packet.SessionID);
            Send((byte[])packet);
        }

        protected virtual void OnMessageReceived(YMSGPacket packet)
        {
            this.SessionID = packet.SessionID;
            if (this.MessageReceived != null)
                this.MessageReceived(packet);
        }

        protected virtual void OnNotifyInformation(YMSGNotifyEventTypes type, object o)
        {
            if (this.NotifyInformation != null)
                this.NotifyInformation(new YMSGNotifyEventArgs(type, o));
        }

        public virtual void Send(byte[] packet)
        {
            isSending.WaitOne();
            this.isConnecting.WaitOne();

            if (this.socket.Connected)
                this.socket.BeginSend(packet, 0, packet.Length, SocketFlags.None, new AsyncCallback(x =>
                {
                    YMSGConnection yc = x.AsyncState as YMSGConnection;
                    int bytesSent = yc.socket.EndSend(x);
                    yc.OnNotifyInformation(YMSGNotifyEventTypes.BytesSent, bytesSent);
                    yc.isSending.Set();
                }), this);
        }

        /// <summary>
        /// Authenticates against CookieY & CookieT and logs on to pager.
        /// </summary>
        public virtual void Logon()
        {
            YMSGPacket pkt = new YMSGPacket() { Service = 550, Status = 12 };

            pkt["0"] = this.LoginName;
            pkt["2"] = this.LoginName;
            pkt["1"] = this.LoginName;
            pkt["244"] = "16777215";
            pkt["6"] = this.CookieY + "; " + this.CookieT + ";";
            pkt["98"] = "us";

            Send(pkt);
        }

        public void SendPM(string id, string message, string infTag = null)
        {
            if (!string.IsNullOrEmpty(infTag))
                message = infTag + message;

            YMSGPacket pkt = new YMSGPacket()
            {
                Service = 6,
                Status = 33
            };

            pkt["1"] = this.LoginName;
            pkt["5"] = id.Trim();
            pkt["14"] = message;
            pkt["97"] = "1";
            pkt["63"] = ";0";
            pkt["64"] = "0";
            pkt["241"] = "0";

            Send(pkt);
        }
        #endregion
    }
}
