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

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Kirin_Tool.Services;

namespace Kirin_Tool.Services.USBUpdate
{
    public class XloaderPatcher
    {
        private static readonly string ResDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fastboot");
        private readonly Action<string> _log;
        private readonly string _dloadDirectory;
        private readonly FastbootClient _fastbootClient;

        public XloaderPatcher(Action<string> log, string dloadDirectory, FastbootClient fastbootClient)
        {
            _log = log;
            _dloadDirectory = dloadDirectory;
            _fastbootClient = fastbootClient;
        }

        public void PatchXloader(string xloaderPath)
        {
            string crcHackedPath = Path.Combine(ResDirectory, "payload");
            
            if (!File.Exists(crcHackedPath))
            {
                throw new Exception($"Crucial file not found");
            }

            byte[] xloaderData = File.ReadAllBytes(xloaderPath);
            byte[] crchacked = File.ReadAllBytes(crcHackedPath);

            if (crchacked.Length != 0x8000)
            {
                throw new Exception("Crucial file has invalid size");
            }

            if (xloaderData.Length < 0x8000)
            {
                throw new Exception("XLOADER.img is too small to patch");
            }

            byte[] a1 = new byte[0x8000];
            Array.Copy(xloaderData, 0, a1, 0, 0x8000);

            byte[] a2 = new byte[xloaderData.Length - 0x8000];
            Array.Copy(xloaderData, 0x8000, a2, 0, a2.Length);

            byte[] c = new byte[0x8000];
            for (int i = 0; i < 0x8000; i++)
            {
                c[i] = (byte)(crchacked[i] ^ a1[i]);
            }

            using (FileStream fs = new FileStream(xloaderPath, FileMode.Create, FileAccess.Write))
            {
                fs.Write(c, 0, c.Length);
                fs.Write(a2, 0, a2.Length);
            }
        }

        public bool VerifyPatch(string originalPath, string patchedPath)
        {
            byte[] originalData = File.ReadAllBytes(originalPath).Take(0x8000).ToArray();
            byte[] patchedData = File.ReadAllBytes(patchedPath).Take(0x8000).ToArray();

            if (originalData.SequenceEqual(patchedData))
            {
                return false;
            }

            ushort originalCrc = Crc16X25.Calculate(originalData);
            ushort patchedCrc = Crc16X25.Calculate(patchedData);

            if (originalCrc != patchedCrc)
            {
                return false;
            }

            return true;
        }

        public void ModifyListTxtStopAfterXloader()
        {
            string listPath = Path.Combine(_dloadDirectory, "list.txt");
            if (!File.Exists(listPath))
                return;

            var lines = File.ReadAllLines(listPath).ToList();
            bool foundXloader = false;

            for (int i = 0; i < lines.Count; i++)
            {
                string[] parts = lines[i].Split(' ');
                if (parts.Length < 2)
                    continue;

                string name = parts[0];

                if (name.Equals("XLOADER", StringComparison.OrdinalIgnoreCase))
                {
                    foundXloader = true;
                    continue;
                }

                if (foundXloader)
                {
                    lines[i] = $"{name} 0";
                }
            }

            File.WriteAllLines(listPath, lines);
        }

        public void RestoreListTxt()
        {
            string listPath = Path.Combine(_dloadDirectory, "list.txt");
            if (!File.Exists(listPath))
                return;

            var lines = File.ReadAllLines(listPath).ToList();

            for (int i = 0; i < lines.Count; i++)
            {
                string[] parts = lines[i].Split(' ');
                if (parts.Length >= 2)
                {
                    string name = parts[0];
                    lines[i] = $"{name} 1";
                }
            }

            File.WriteAllLines(listPath, lines);
        }

        public async Task FlashXloaderViaFastbootAsync(string xloaderPath)
        {

            var result = await _fastbootClient.FlashPartition("xloader", xloaderPath);
            
            if (!result.IsSuccess)
            {
                throw new Exception($"Fastboot failed: {result.Output}");
            }
        }

        public async Task RebootViaFastbootAsync()
        {
            await _fastbootClient.RebootAsync();
        }
    }
}
