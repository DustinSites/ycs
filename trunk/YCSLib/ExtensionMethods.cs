﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;

namespace YCSLib
{
    public static class ExtensionMethods
    {
        public static YCSLib.YMSGPacket.Payload Slice(this YCSLib.YMSGPacket.Payload payload, string key, int index)
        {
            YCSLib.YMSGPacket.Payload retVal = null;
            int x = 0;
            for (int i = 0; i < payload.Count; i++)
                if (payload[index].Key == key)
                    if (x == index)
                        retVal = new YCSLib.YMSGPacket.Payload(payload.GetRange(x, payload.FindIndex(x, p =>
                        { if (p.Key == key) return true; return false; }
                        )));
                    else
                        x++;
            return retVal;
        }

        [DebuggerStepThrough]
        private static unsafe bool ExactMatch(byte* pSrc, byte[] pattern)
        {
            int i = 0;
            while (i < pattern.Length)
                if (pattern[i] != *(pSrc + i))
                    return false;
                else
                    i++;
            return true;
        }

        /// <summary>
        /// KMP: leaving it here 'cause the match is trivial. Boyer Moore next?
        /// </summary>
        /// <param name="bytes">source array</param>
        /// <param name="pattern">pattern to match</param>
        /// <param name="startIndex"></param>
        /// <returns>index of match found</returns>
        [DebuggerStepThrough]
        internal static unsafe int FindIndex(this byte[] bytes, byte[] pattern, int startIndex = 0)
        {
            if ((bytes.Length - startIndex) < pattern.Length)
                return -1;

            fixed (byte* pBytes = bytes)
            {
                int i = startIndex;
                while (i < bytes.Length)
                {
                    if (ExactMatch((pBytes + i), pattern))
                        return (i - startIndex);
                    i += 1;
                }
            }

            return -1;
        }

        internal static byte[] Slice(this byte[] bytes, int length, int startIndex = 0)
        {
            return bytes.Skip(startIndex).Take(length).ToArray();
        }
    }
}
