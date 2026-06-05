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
using System.Text;
using System.Threading.Tasks;
using Kirin_Tool.Services;

namespace Kirin_Tool.Services.USBUpdate
{
    public class SoftwareTestpointService
    {
        private readonly Action<string> _log;
        private readonly string _dloadDirectory;
        private readonly FastbootClient _fastbootClient;
        private readonly Func<string, string, Task<bool>> _onRetryRequired;

        public SoftwareTestpointService(Action<string> log, string dloadDirectory, FastbootClient fastbootClient, Func<string, string, Task<bool>> onRetryRequired)
        {
            _log = log;
            _dloadDirectory = dloadDirectory;
            _fastbootClient = fastbootClient;
            _onRetryRequired = onRetryRequired;
        }

        public async Task EnterSoftwareTestpoint(string updateAppPath)
        {
            if (Directory.Exists(_dloadDirectory))
            {
                try { Directory.Delete(_dloadDirectory, true); } catch { }
            }
            Directory.CreateDirectory(_dloadDirectory);

            var extractor = new USBUpdateApp(_log, _dloadDirectory);
            var patcher = new XloaderPatcher(_log, _dloadDirectory, _fastbootClient);
            var usbService = new UsbDownloadService(_log, _dloadDirectory);

            usbService.OnRetryRequired += (title, message) =>
            {
                return _onRetryRequired(title, message).GetAwaiter().GetResult();
            };


            extractor.ExtractUpToXloader(updateAppPath);

            string xloaderPath = Path.Combine(_dloadDirectory, "XLOADER.img");
            if (!File.Exists(xloaderPath))
            {
                throw new Exception("Extraction failed, XLOADER not found in Base UPDATE.");
            }


            string xloaderBackupPath = Path.Combine(_dloadDirectory, "XLOADER_BAK.img");
            File.Copy(xloaderPath, xloaderBackupPath, true);

            try
            {
                patcher.PatchXloader(xloaderPath);

                if (!patcher.VerifyPatch(xloaderBackupPath, xloaderPath))
                {
                    throw new Exception("Failed to patch XLOADER");
                }


                patcher.ModifyListTxtStopAfterXloader();


                bool success = await Task.Run(() => usbService.FlashImages());
                if (!success)
                {
                    throw new Exception("Failed to Software Testpoint the device!\n\nMake sure you are using the Base UPDATE from the exact or newer firmware that your device is running.");
                }


            }
            finally
            {
                if (File.Exists(xloaderBackupPath))
                {
                    File.Copy(xloaderBackupPath, xloaderPath, true);
                    File.Delete(xloaderBackupPath);
                }
            }
        }

        public async Task ExitSoftwareTestpoint(string updateAppPath)
        {
            if (Directory.Exists(_dloadDirectory))
            {
                try { Directory.Delete(_dloadDirectory, true); } catch { }
            }
            Directory.CreateDirectory(_dloadDirectory);

            var extractor = new USBUpdateApp(_log, _dloadDirectory);
            var patcher = new XloaderPatcher(_log, _dloadDirectory, _fastbootClient);


            extractor.ExtractSinglePartition(updateAppPath, "XLOADER");

            string xloaderPath = Path.Combine(_dloadDirectory, "XLOADER.img");
            if (!File.Exists(xloaderPath))
            {
                throw new Exception("XLOADER.img extraction failed");
            }


            await patcher.FlashXloaderViaFastbootAsync(xloaderPath);
        }
    }
}
