// Licensed to Kethily Daniel & NDXCode under one or more agreements.
// Kethily Daniel & NDXCode licenses this file to you under the Business Source License 1.1.
// See the LICENSE file in the project root for more information.

/*
 * Copyright (c) 2026 Kethily Daniel & NDXCode. All rights reserved.
 * 
 * Use of this software is governed by the Business Source License included 
 * in the LICENSE file and at www.mariadb.com/bsl11.
 * 
 * Change Date: Four years from the date each version of the Licensed Work 
 * is first publicly distributed.
 * 
 * On the Change Date, in accordance with the Business Source License, 
 * use of this software will be governed by the GNU General Public License v3.0 
 * or later (GPL-3.0-or-later).
 * 
 * Contact Information: https://kirintool.cfd
 */

using System.IO;
using System.IO.Compression;

namespace Kirin_Tool.Services.USBUpdate
{
    public static class Compression
    {
        public static byte[] ZlibCompress(byte[] data, int offset, int length)
        {
            using (MemoryStream output = new MemoryStream())
            {
                output.WriteByte(0x78);
                output.WriteByte(0x01);
                
                using (DeflateStream deflate = new DeflateStream(output, CompressionLevel.Fastest, leaveOpen: true))
                {
                    deflate.Write(data, offset, length);
                }
                
                uint adler = CalculateAdler32(data, offset, length);
                output.WriteByte((byte)(adler >> 24));
                output.WriteByte((byte)(adler >> 16));
                output.WriteByte((byte)(adler >> 8));
                output.WriteByte((byte)adler);
                
                return output.ToArray();
            }
        }

        public static byte[] ZlibCompress(byte[] data)
        {
            return ZlibCompress(data, 0, data.Length);
        }

        public static uint CalculateAdler32(byte[] data, int offset, int length)
        {
            const uint MOD_ADLER = 65521;
            uint a = 1, b = 0;
            
            for (int i = offset; i < offset + length; i++)
            {
                a = (a + data[i]) % MOD_ADLER;
                b = (b + a) % MOD_ADLER;
            }
            
            return (b << 16) | a;
        }

        public static uint CalculateAdler32(byte[] data)
        {
            return CalculateAdler32(data, 0, data.Length);
        }
    }
}
