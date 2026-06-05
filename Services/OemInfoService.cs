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
using System.Threading.Tasks;
using Kirin_Tool.Models;

namespace Kirin_Tool.Services
{
    public class OemInfoService
    {
        private readonly FastbootClient _fastbootClient;
        private readonly string _tempOemInfoPath = Path.Combine(Path.GetTempPath(), "temp_oeminfo.img");

        public OemInfoService(FastbootClient fastbootClient)
        {
            _fastbootClient = fastbootClient;
        }

        public async Task<OemInfoConversionResult> ConvertOemInfo(
            bool pullFromDevice,
            string inputFilePath,
            string model,
            string vendorCountry,
            IProgress<string> progress = null)
        {
            var result = new OemInfoConversionResult();

            try
            {
                string sourceFilePath;
                if (pullFromDevice)
                {
                    progress?.Report("Pulling OEMInfo from device...");
                    var pullResult = await _fastbootClient.PullOemInfo(_tempOemInfoPath);
                    if (!pullResult.IsSuccess || !File.Exists(_tempOemInfoPath))
                    {
                        result.ErrorMessage = "Failed to pull OEMInfo from device. Make sure device is connected in fastboot mode.";
                        return result;
                    }
                    sourceFilePath = _tempOemInfoPath;
                    result.PulledFromDevice = true;
                }
                else
                {
                    if (string.IsNullOrEmpty(inputFilePath) || !File.Exists(inputFilePath))
                    {
                        result.ErrorMessage = "Input OEMInfo file does not exist.";
                        return result;
                    }
                    sourceFilePath = inputFilePath;
                }

                progress?.Report("Converting OEMInfo...");
                var originalData = await File.ReadAllBytesAsync(sourceFilePath);
                var editor = new OemInfoEditor(originalData);
                var modifiedData = editor.EditEntries(model, vendorCountry);

                result.ConvertedData = modifiedData;
                result.IsSuccess = true;
                progress?.Report("Conversion completed successfully!");

                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"Error during conversion: {ex.Message}";
                return result;
            }
        }

        public async Task<ProcessResult> FlashConvertedOemInfo(string filePath)
        {
            return await _fastbootClient.FlashOemInfo(filePath);
        }

        public void CleanupTempFiles()
        {
            try
            {
                if (File.Exists(_tempOemInfoPath))
                {
                    File.Delete(_tempOemInfoPath);
                }
            }
            catch
            {
            }
        }
    }
}
