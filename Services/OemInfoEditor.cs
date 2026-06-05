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

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Kirin_Tool.Services
{
    public class OemInfoEditor
    {
        private const int BlockSize = 0x400;
        private const int DataPayloadOffsetInBlock = 0x200;
        private static readonly byte[] MagicBytes = Encoding.ASCII.GetBytes("OEM_INFO");

        private readonly byte[] _binaryData;
        private readonly int _detectedVersion;

        private static readonly Dictionary<int, (List<int> ModelIds, int RegionId)> TargetIdsMap = new Dictionary<int, (List<int>, int)>
        {
            { 6, (new List<int>{0x5b, 0x61}, 0x12) },
            { 8, (new List<int>{0x5ee, 0x5ef}, 0x5de) },
            { 9, (new List<int>{0x301, 0x300}, 0x2f0) }
        };

        public OemInfoEditor(byte[] oeminfoData)
        {
            _binaryData = oeminfoData;
            _detectedVersion = DetectOemVersion();
            if (_detectedVersion == -1)
            {
                throw new InvalidDataException("Could not detect a supported OEMINFO version.");
            }
        }

        private int DetectOemVersion()
        {
            for (int offset = 0; offset < _binaryData.Length; offset += BlockSize)
            {
                if (offset + 12 > _binaryData.Length) break;

                if (_binaryData.Skip(offset).Take(8).SequenceEqual(MagicBytes))
                {
                    return System.BitConverter.ToInt32(_binaryData, offset + 8);
                }
            }
            return -1;
        }

        public byte[] EditEntries(string model, string regionVendor)
        {
            if (!TargetIdsMap.ContainsKey(_detectedVersion))
                throw new System.NotSupportedException($"OEMINFO version '{_detectedVersion}' is not supported for modification.");

            var targets = TargetIdsMap[_detectedVersion];
            var modifiedData = (byte[])_binaryData.Clone();

            for (int offset = 0; offset < modifiedData.Length; offset += BlockSize)
            {
                if (offset + 16 > modifiedData.Length) break;
                if (!modifiedData.Skip(offset).Take(8).SequenceEqual(MagicBytes)) continue;

                int entryVersion = System.BitConverter.ToInt32(modifiedData, offset + 8);
                int entryId = System.BitConverter.ToInt32(modifiedData, offset + 12);

                if (entryVersion == _detectedVersion)
                {
                    if (targets.ModelIds.Contains(entryId))
                        UpdateEntry(modifiedData, offset, model);
                    else if (targets.RegionId == entryId)
                        UpdateEntry(modifiedData, offset, regionVendor);
                }
            }
            return modifiedData;
        }

        private void UpdateEntry(byte[] data, int blockOffset, string newString)
        {
            var newBytes = Encoding.UTF8.GetBytes(newString);
            int newLen = newBytes.Length;

            System.Buffer.BlockCopy(System.BitConverter.GetBytes(newLen), 0, data, blockOffset + 20, 4);

            int dataStartOffset = blockOffset + DataPayloadOffsetInBlock;
            System.Buffer.BlockCopy(newBytes, 0, data, dataStartOffset, newLen);

            int paddingStart = dataStartOffset + newLen;
            int maxPayloadSize = BlockSize - DataPayloadOffsetInBlock;
            for (int i = paddingStart; i < blockOffset + DataPayloadOffsetInBlock + maxPayloadSize; i++)
            {
                data[i] = 0xFF;
            }
        }
    }
}