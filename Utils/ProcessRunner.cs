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
using System.Diagnostics;
using System.Threading.Tasks;
using System;
using System.Threading;
using System.IO;

namespace Kirin_Tool.Utils
{
    public static class ProcessRunner
    {
        private static readonly object LogLock = new object();

        public static async Task<ProcessResult> RunAsync(string executablePath, string arguments, int timeoutMinutes = 300, int timeoutSeconds = 0)
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = new Process { StartInfo = processStartInfo };
            process.Start();

            var timeout = timeoutSeconds > 0 ? TimeSpan.FromSeconds(timeoutSeconds) : TimeSpan.FromMinutes(timeoutMinutes);
            using var cts = new CancellationTokenSource(timeout);

            try
            {
                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();

                await process.WaitForExitAsync(cts.Token);

                string output = await outputTask;
                string error = await errorTask;

                var result = new ProcessResult
                {
                    ExitCode = process.ExitCode,
                    Output = output + error,
                    Arguments = arguments
                };

                WriteLog(executablePath, arguments, result.Output);

                return result;
            }
            catch (OperationCanceledException)
            {
                process.Kill();
                WriteLog(executablePath, arguments, $"[Process Timed Out after {timeout.TotalSeconds} seconds]");
                throw new TimeoutException($"Process timed out after {timeoutMinutes} minutes");
            }
        }

        private static void WriteLog(string executablePath, string arguments, string output)
        {
            try
            {
                if (!executablePath.Contains("fastboot", StringComparison.OrdinalIgnoreCase))
                    return;

                string exeDir = AppDomain.CurrentDomain.BaseDirectory;
                string logDir = Path.Combine(exeDir, "log");
                if (!Directory.Exists(logDir))
                {
                    Directory.CreateDirectory(logDir);
                }

                string fileName = $"kirintool_log_{DateTime.Now:yyyy_MM_dd}.log";
                string logFilePath = Path.Combine(logDir, fileName);

                string timestamp = DateTime.Now.ToString("HH:mm:ss");
                string cmdText = $"{Path.GetFileName(executablePath)} {arguments}";
                string logMessage = $"[{timestamp}] {cmdText}\n{output}\n\n";

                lock (LogLock)
                {
                    File.AppendAllText(logFilePath, logMessage);
                }
            }
            catch {}
        }
    }
}