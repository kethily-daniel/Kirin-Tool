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

namespace Kirin_Tool.Utils
{
    public static class Crc16
    {
        private static readonly ushort[] HqxTable = new ushort[256];

        static Crc16()
        {
            ushort hqx_poly = 0x1021;
            for (int i = 0; i < 256; i++)
            {
                ushort hqx_crc = (ushort)(i << 8);
                for (int j = 0; j < 8; j++)
                {
                    if ((hqx_crc & 0x8000) != 0)
                        hqx_crc = (ushort)((hqx_crc << 1) ^ hqx_poly);
                    else
                        hqx_crc <<= 1;
                }
                HqxTable[i] = hqx_crc;
            }
        }

        public static ushort CrcHqx(byte[] data)
        {
            ushort crc = 0;

            foreach (byte b in data)
            {
                ushort highByte = (ushort)(crc >> 8);
                ushort tableValue = HqxTable[highByte];
                crc = (ushort)((crc << 8) | b);
                crc ^= tableValue;
            }

            for (int i = 0; i < 2; i++)
            {
                ushort highByte = (ushort)(crc >> 8);
                ushort tableValue = HqxTable[highByte];
                crc <<= 8;
                crc ^= tableValue;
            }

            return crc;
        }
    }
}