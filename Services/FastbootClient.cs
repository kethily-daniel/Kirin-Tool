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

using Kirin_Tool.Models;
using Kirin_Tool.Utils;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Kirin_Tool.Services
{
    public class FastbootClient
    {
        private readonly string _fastbootPath;

        public FastbootClient(string executablePath = "fastboot/fastboot.exe")
        {
            _fastbootPath = Path.Combine(Directory.GetCurrentDirectory(), executablePath);
        }

        public async Task<bool> IsDeviceConnected()
        {
            if (!File.Exists(_fastbootPath)) return false;
            var result = await ProcessRunner.RunAsync(_fastbootPath, "devices");
            return result.IsSuccess && result.Output.Contains("fastboot");
        }

        public async Task<string> ReadNveVariable(string variable)
        {
            var result = await ProcessRunner.RunAsync(_fastbootPath, $"getvar nve:{variable}");
            if (result.IsSuccess && result.Output.Contains($"nve:{variable}:"))
            {
                return result.Output.Split('\n')
                    .FirstOrDefault(line => line.StartsWith($"nve:{variable}:"))
                    ?.Substring($"nve:{variable}:".Length).Trim() ?? string.Empty;
            }
            return string.Empty;
        }

        public async Task<ProcessResult> WriteNveVariable(string variable, string value)
        {
            return await ProcessRunner.RunAsync(_fastbootPath, $"getvar nve:{variable}@{value}");
        }

        public async Task<ProcessResult> FlashPartition(string partitionName, string filePath)
        {
            return await ProcessRunner.RunAsync(_fastbootPath, $"flash {partitionName} \"{filePath}\"");
        }

        public async Task<FrpBypassResult> EraseFrpWithSteps()
        {
            var result = new FrpBypassResult();

            var step1Result = await ProcessRunner.RunAsync(_fastbootPath, "erase frp");
            result.Step1Success = step1Result.IsSuccess && step1Result.Output.ToUpper().Contains("OKAY");
            result.Step1Output = step1Result.Output;

            var step2Result = await ProcessRunner.RunAsync(_fastbootPath, "erase config");
            result.Step2Success = step2Result.IsSuccess && step2Result.Output.ToUpper().Contains("OKAY");
            result.Step2Output = step2Result.Output;

            var step3Result = await ProcessRunner.RunAsync(_fastbootPath, "oem frp-erase");
            result.Step3Success = step3Result.IsSuccess && step3Result.Output.ToUpper().Contains("OKAY");
            result.Step3Output = step3Result.Output;


            return result;
        }

        public async Task<EnableDowngradeResult> EnableDowngradeWithSteps()
        {
            var result = new EnableDowngradeResult();

            var step1Result = await ProcessRunner.RunAsync(_fastbootPath, "oem oeminfoerase-amssver");
            result.Step1Success = step1Result.IsSuccess && step1Result.Output.ToUpper().Contains("OKAY");
            result.Step1Output = step1Result.Output;

            var step2Result = await ProcessRunner.RunAsync(_fastbootPath, "oem oeminfoerase-basever");
            result.Step2Success = step2Result.IsSuccess && step2Result.Output.ToUpper().Contains("OKAY");
            result.Step2Output = step2Result.Output;

            var step3Result = await ProcessRunner.RunAsync(_fastbootPath, "oem oeminfoerase-custver");
            result.Step3Success = step3Result.IsSuccess && step3Result.Output.ToUpper().Contains("OKAY");
            result.Step3Output = step3Result.Output;

            var step4Result = await ProcessRunner.RunAsync(_fastbootPath, "oem oeminfoerase-preloadver");
            result.Step4Success = step4Result.IsSuccess && step4Result.Output.ToUpper().Contains("OKAY");
            result.Step4Output = step4Result.Output;

            return result;
        }

        public async Task<ProcessResult> PullOemInfo(string outputPath)
        {
            return await ProcessRunner.RunAsync(_fastbootPath, $"oem dump-emmc oeminfo \"{outputPath}\"");
        }

        public async Task<ProcessResult> FlashOemInfo(string filePath)
        {
            return await ProcessRunner.RunAsync(_fastbootPath, $"flash oeminfo \"{filePath}\"");
        }

        public async Task<string> GetVarAsync(string variable)
        {
            var result = await ProcessRunner.RunAsync(_fastbootPath, $"getvar {variable}", timeoutSeconds: 10);
            return result.Output;
        }

        public async Task<string> OemCommandAsync(string command, int timeoutMinutes = 300)
        {
            var result = await ProcessRunner.RunAsync(_fastbootPath, $"oem {command}", timeoutMinutes: timeoutMinutes);
            return result.Output;
        }

        public async Task<string> CommandAsync(string arguments, int timeoutMinutes = 300)
        {
            var result = await ProcessRunner.RunAsync(_fastbootPath, arguments, timeoutMinutes: timeoutMinutes);
            return result.Output;
        }

        public async Task<string> RebootBootloaderAsync()
        {
            return await CommandAsync("reboot-bootloader");
        }

        public async Task<(bool IsSuccess, string Output)> UnlockBootloaderAsync(IProgress<string> progress = null)
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = _fastbootPath,
                Arguments = "oem unlock UUUUUUUUUUUUUUUU",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = new Process { StartInfo = processStartInfo };
            
            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();
            var combinedBuilder = new StringBuilder();
            var outputLock = new object();

            process.Start();

            var outputTask = Task.Run(async () =>
            {
                try
                {
                    using var reader = process.StandardOutput;
                    char[] buffer = new char[1];
                    while (true)
                    {
                        int read = await reader.ReadAsync(buffer, 0, 1);
                        if (read == 0)
                        {
                            break;
                        }
                        
                        char c = buffer[0];
                        
                        lock (outputLock)
                        {
                            outputBuilder.Append(c);
                            combinedBuilder.Append(c);
                            progress?.Report(combinedBuilder.ToString());
                        }
                    }
                }
                catch (Exception ex)
                {
                }
            });

            var errorTask = Task.Run(async () =>
            {
                try
                {
                    using var reader = process.StandardError;
                    char[] buffer = new char[1];
                    while (true)
                    {
                        int read = await reader.ReadAsync(buffer, 0, 1);
                        if (read == 0)
                        {
                            break;
                        }
                        
                        char c = buffer[0];
                        
                        lock (outputLock)
                        {
                            errorBuilder.Append(c);
                            combinedBuilder.Append(c);
                            progress?.Report(combinedBuilder.ToString());
                        }
                    }
                }
                catch (Exception ex)
                {
                }
            });

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(300));
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill();
                return (false, "Process timed out after 5 minutes");
            }

            await Task.Delay(500);
            try
            {
                await Task.WhenAny(outputTask, errorTask, Task.Delay(2000));
            }
            catch { }

            string output = combinedBuilder.ToString();
            return (process.ExitCode == 0, output);
        }

        public async Task<ProcessResult> RebootAsync()
        {
            return await ProcessRunner.RunAsync(_fastbootPath, "reboot");
        }
    }
}