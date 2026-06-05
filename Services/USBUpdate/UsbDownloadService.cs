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
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Management;
using System.Threading;
using Kirin_Tool.Models;

namespace Kirin_Tool.Services.USBUpdate
{
    public class UsbDownloadService
    {
        private readonly Action<string> _log;
        private readonly string _dloadDirectory;

        public Func<string, string, bool>? OnRetryRequired;
        public Action<int>? OnProgressUpdate;
        public Action<int, string>? OnPartitionStarted;
        public Action<int, string, int>? OnPartitionProgressUpdate;
        public Action<int, string, bool, string>? OnPartitionCompleted;

        public UsbDownloadService(Action<string> log, string dloadDirectory)
        {
            _log = log;
            _dloadDirectory = dloadDirectory;
        }

        public bool FlashImages()
        {
            SerialPort port = null;
            
            while (port == null)
            {
                port = BuildConnection();
                if (port == null)
                {
                    var result = OnRetryRequired?.Invoke("Device Not Found", 
                        "No HiSilicon device found in USB Update Mode.\n\nConnect the device and click Done! to retry, or Cancel to abort.");

                    if(result == false) {
                        throw new OperationCanceledException("USB Update cancelled by user.");
                    }
                }
            }

            bool success = true;

            try
            {
                DoHandshake(port);
                
                SendUnlockCommand(port);
                
                success = FlashPartitions(port);

                if (success)
                {
                    SendCommand(port, CreateRebootCommand(), 0.3);
                    Thread.Sleep(50);
                    SendCommand(port, CreateForceRebootCommand(), 0.3);
                }
                else
                {
                }
            }
            finally
            {
                port?.Close();
                port?.Dispose();
            }
            return success;
        }

        private SerialPort BuildConnection()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity WHERE Caption LIKE '%(COM%'"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        string caption = obj["Caption"]?.ToString() ?? "";
                        string deviceID = obj["DeviceID"]?.ToString() ?? "";

                        if (deviceID.Contains("VID_12D1") && caption.Contains("DBAdapter Reserved Interface"))
                        {
                            int startIndex = caption.IndexOf("(COM") + 1;
                            int endIndex = caption.IndexOf(")", startIndex);
                            string portName = caption.Substring(startIndex, endIndex - startIndex);


                            SerialPort port = new SerialPort(portName, 9600)
                            {
                                ReadTimeout = 5000,
                                WriteTimeout = 5000,
                                WriteBufferSize = 8 * 1024 * 1024,
                                ReadBufferSize = 1024 * 1024
                            };

                            port.Open();
                            return port;
                        }
                    }
                }
            }
            catch (Exception ex) {}

            return null;
        }

        private void DoHandshake(SerialPort port)
        {
            const int maxRetries = 3;
            byte[] expectedPrefix = new byte[] { 0x7E, 0x26, 0x00, 0x00, 0x25, 0xA7 };

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                byte[] cmd = CreateHandshakeCommand();
                port.Write(cmd, 0, cmd.Length);
                Thread.Sleep(50);

                byte[] response = ReadResponse(port);
                
                if (ContainsSequence(response, expectedPrefix))
                {
                    return;
                }

                if (attempt < maxRetries)
                {
                    Thread.Sleep(200);
                }
            }

        }

        private void SendUnlockCommand(SerialPort port)
        {
            string unlockPath = Path.Combine(_dloadDirectory, "unlockcode");
            if (!File.Exists(unlockPath))
            {
                return;
            }

            byte[] unlockCode = File.ReadAllBytes(unlockPath);

            List<byte> cmd = new List<byte> { 0x0B };
            cmd.AddRange(unlockCode);

            byte[] crc = Crc16X25.CalculateBytes(cmd.ToArray());
            cmd.AddRange(crc);

            byte[] converted = ConvertData(cmd.ToArray());

            List<byte> finalCmd = new List<byte> { 0x7E };
            finalCmd.AddRange(converted);
            finalCmd.Add(0x7E);

            if (!SendCommand(port, finalCmd.ToArray(), 0.1)) {}
        }

        private bool FlashPartitions(SerialPort port)
        {
            bool allSuccess = true;
            string listPath = Path.Combine(_dloadDirectory, "list.txt");
            if (!File.Exists(listPath))
            {
                return false;
            }

            var lines = File.ReadAllLines(listPath);
            
            Dictionary<int, string> partitionSourceMap = new Dictionary<int, string>();
            string mappingPath = Path.Combine(_dloadDirectory, "partition_mapping.txt");
            if (File.Exists(mappingPath))
            {
                var mappingLines = File.ReadAllLines(mappingPath);
                for (int i = 0; i < mappingLines.Length && i < lines.Length; i++)
                {
                    var mappingParts = mappingLines[i].Split('|');
                    if (mappingParts.Length >= 2)
                    {
                        partitionSourceMap[i] = mappingParts[1];
                    }
                }
            }

            int lineIndex = 0;
            foreach (var line in lines)
            {
                var parts = line.Split(' ');
                if (parts.Length < 2)
                    continue;

                string name = parts[0];
                int flag = 0;
                int.TryParse(parts[1], out flag);

                if (flag != 1)
                {
                    lineIndex++;
                    continue;
                }

                if (partitionSourceMap.ContainsKey(lineIndex))
                {
                    string sourceDir = partitionSourceMap[lineIndex];
                    string sourceImg = Path.Combine(sourceDir, $"{name}.img");
                    string sourceHeader = Path.Combine(sourceDir, $"{name}.img.header");
                    string destImg = Path.Combine(_dloadDirectory, $"{name}.img");
                    string destHeader = Path.Combine(_dloadDirectory, $"{name}.img.header");
                    
                    if (File.Exists(sourceImg))
                    {
                        long sourceSize = new FileInfo(sourceImg).Length;
                        if (sourceSize == 0)
                        {
                            lineIndex++;
                            continue;
                        }
                        File.Copy(sourceImg, destImg, true);
                    }
                    if (File.Exists(sourceHeader))
                    {
                        File.Copy(sourceHeader, destHeader, true);
                    }
                }

                string imgPath = Path.Combine(_dloadDirectory, $"{name}.img");
                string headerPath = Path.Combine(_dloadDirectory, $"{name}.img.header");
                
                if (!File.Exists(imgPath))
                {
                    lineIndex++;
                    continue;
                }
                
                if (!File.Exists(headerPath))
                {
                    lineIndex++;
                    continue;
                }
                
                long fileSize = new FileInfo(imgPath).Length;
                if (fileSize == 0)
                {
                    lineIndex++;
                    continue;
                }

                OnPartitionStarted?.Invoke(lineIndex, name);
                OnPartitionProgressUpdate?.Invoke(lineIndex, name, 0);

                double fileSizeMB = fileSize / 1024.0 / 1024.0;
                double tailTimeout = Math.Max(35.0, Math.Min(180.0, 15.0 + (fileSizeMB / 10.0)));
                
                byte[] header = ReadHeader(name);
                string? headError = SendCommandInternal(port, CreateHeadCommand(header), 2.0);
                if (headError != null)
                {
                    throw new Exception($"Failed to send partition header.");
                }
                
                int blockSize = 0x20000;
                try
                {
                    string? imageError = SendImage(port, lineIndex, name, blockSize);
                    if (imageError != null)
                    {
                        throw new Exception($"Failed to send partition data.");
                    }
                    string? tailError = SendCommandInternal(port, CreateTailCommand(header), tailTimeout);
                    if (tailError != null)
                    {
                        throw new Exception($"Failed to finalize partition.");
                    }
                    OnPartitionCompleted?.Invoke(lineIndex, name, true, "Success");
                }
                catch (Exception ex)
                {
                    OnPartitionCompleted?.Invoke(lineIndex, name, false, ex.Message);
                    return false;
                }
                
                lineIndex++;
            }
            return allSuccess;
        }

        private string? SendImage(SerialPort port, int lineIndex, string name, int blockSize)
        {
            string imgPath = Path.Combine(_dloadDirectory, $"{name}.img");
            long fileSize = new FileInfo(imgPath).Length;
            uint addr = 0;

            byte[] header = ReadHeader(name);
            byte[] fileSeq = new byte[4];
            if (header.Length >= 24)
            {
                Array.Copy(header, 20, fileSeq, 0, 4);
                Array.Reverse(fileSeq);
            }

            using (FileStream fs = new FileStream(imgPath, FileMode.Open, FileAccess.Read))
            {
                long remaining = fileSize;
                long totalSent = 0;
                
                byte[] readBuffer = new byte[blockSize];
                int lastReportedProgress = -1;

                while (remaining > 0)
                {
                    int toRead = (int)Math.Min(blockSize, remaining);
                    int bytesRead = fs.Read(readBuffer, 0, toRead);
                    if (bytesRead == 0) break;

                    byte[] compressed = Compression.ZlibCompress(readBuffer, 0, bytesRead);
                    byte[] dataCmd = CreateDataCommand(compressed, bytesRead, fileSeq, addr);

                    double compressedSizeMB = compressed.Length / 1024.0 / 1024.0;
                    double timeout = Math.Max(1.0, Math.Min(8.0, compressedSizeMB * 1.5));
                    
                    string? dataError = SendCommandInternal(port, dataCmd, timeout);
                    if (dataError != null)
                    {
                        return dataError;
                    }

                    addr += (uint)toRead;
                    totalSent += toRead;
                    remaining -= toRead;

                    if (fileSize > 0)
                    {
                        int progress = (int)((totalSent * 100) / fileSize);
                        if (progress != lastReportedProgress)
                        {
                            lastReportedProgress = progress;
                            OnProgressUpdate?.Invoke(progress);
                            OnPartitionProgressUpdate?.Invoke(lineIndex, name, progress);
                        }
                    }
                }
            }
            return null;
        }

        private byte[] ReadHeader(string name)
        {
            string headerPath = Path.Combine(_dloadDirectory, $"{name}.img.header");
            byte[] header = File.ReadAllBytes(headerPath);
            
            if (header.Length > 93)
            {
                header[92] = 0;
                header[93] = 0;
            }
            
            byte[] result = new byte[header.Length + 1];
            Array.Copy(header, result, header.Length);
            result[header.Length] = 0x00;
            
            return result;
        }

        private bool SendCommand(SerialPort port, byte[] cmd, double timeout)
        {
            string? error = SendCommandInternal(port, cmd, timeout);
            if (error != null)
            {
                return false;
            }
            return true;
        }

        private string? SendCommandInternal(SerialPort port, byte[] cmd, double timeout)
        {
            int timeoutMs = (int)(timeout * 1000);

            int offset = 0;
            port.DiscardInBuffer();
            while (offset < cmd.Length)
            {
                int toSend = Math.Min(0x10000, cmd.Length - offset);
                port.Write(cmd, offset, toSend);
                offset += toSend;
            }

            List<byte> responseList = new List<byte>();
            int originalTimeout = port.ReadTimeout;
            port.ReadTimeout = Math.Max(1, timeoutMs);
            
            try
            {
                while (true)
                {
                    int val = port.ReadByte();
                    if (val == -1) break;
                    
                    if (responseList.Count == 0 && val != 0x7E) continue;
                    
                    responseList.Add((byte)val);
                    if (responseList.Count >= 2 && responseList[0] == 0x7E && val == 0x7E)
                    {
                        break;
                    }
                }
            }
            catch (TimeoutException)
            {
            }
            finally
            {
                port.ReadTimeout = originalTimeout;
            }
            
            byte[] response = responseList.ToArray();

            byte[] expectedResponse = new byte[] { 0x7E, 0x02, 0x6A, 0xD3, 0x7E };
            
            if (response.SequenceEqual(expectedResponse))
            {
            }
            else if (response.Length >= expectedResponse.Length && ContainsSequence(response, expectedResponse))
            {
            }
            else
            {
                if (response.Length > 0)
                {
                    string responseHex = string.Join(" ", response.Select(b => b.ToString("X2")));
                    if (response.Length >= 2 && response[0] == 0x7E && response[1] == 0x03)
                    {
                        return $"Device error (0x03). Response: {responseHex}";
                    }
                    return $"Unexpected respond: {responseHex}";
                }
                else
                {
                    return $"No respond within {timeoutMs}ms";
                }
            }

            Thread.Sleep(10);
            return null;
        }

        private byte[] ReadResponse(SerialPort port)
        {
            int bytesToRead = port.BytesToRead;
            if (bytesToRead > 0)
            {
                byte[] buffer = new byte[bytesToRead];
                port.Read(buffer, 0, bytesToRead);
                return buffer;
            }
            return new byte[0];
        }

        private bool ContainsSequence(byte[] source, byte[] pattern)
        {
            if (source == null || pattern == null || source.Length < pattern.Length)
                return false;

            for (int i = 0; i <= source.Length - pattern.Length; i++)
            {
                bool found = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (source[i + j] != pattern[j])
                    {
                        found = false;
                        break;
                    }
                }
                if (found)
                    return true;
            }
            return false;
        }

        private byte[] ConvertData(byte[] data)
        {
            byte[] result = new byte[data.Length * 2];
            int idx = 0;
            
            for (int i = 0; i < data.Length; i++)
            {
                byte b = data[i];
                if (b == 0x7E)
                {
                    result[idx++] = 0x7D;
                    result[idx++] = 0x5E;
                }
                else if (b == 0x7D)
                {
                    result[idx++] = 0x7D;
                    result[idx++] = 0x5D;
                }
                else
                {
                    result[idx++] = b;
                }
            }

            byte[] finalResult = new byte[idx];
            Array.Copy(result, finalResult, idx);
            return finalResult;
        }

        private byte[] CreateHandshakeCommand()
        {
            byte[] cmd = new byte[] { 0x26, 0x00, 0x00, 0x25, 0xA7, 0x00, 0x06, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00 };
            byte[] crc = Crc16X25.CalculateBytes(cmd);

            List<byte> result = new List<byte>(cmd);
            result.AddRange(crc);
            result.Add(0x7E);

            return result.ToArray();
        }

        private byte[] CreateHeadCommand(byte[] headerData)
        {
            List<byte> cmd = new List<byte> { 0x41 };
            cmd.AddRange(headerData);

            byte[] crc = Crc16X25.CalculateBytes(cmd.ToArray());
            cmd.AddRange(crc);

            byte[] converted = ConvertData(cmd.ToArray());

            List<byte> result = new List<byte> { 0x7E };
            result.AddRange(converted);
            result.Add(0x7E);

            return result.ToArray();
        }

        private byte[] CreateTailCommand(byte[] headerData)
        {
            List<byte> cmd = new List<byte> { 0x43 };
            cmd.AddRange(headerData);

            byte[] crc = Crc16X25.CalculateBytes(cmd.ToArray());
            cmd.AddRange(crc);

            byte[] converted = ConvertData(cmd.ToArray());

            List<byte> result = new List<byte> { 0x7E };
            result.AddRange(converted);
            result.Add(0x7E);

            return result.ToArray();
        }

        private byte[] CreateDataCommand(byte[] compressedData, int originalLength, byte[] fileSeq, uint addr)
        {
            List<byte> cmd = new List<byte> { 0x0F };

            uint fileSeqInt = (uint)((fileSeq[0] << 24) | (fileSeq[1] << 16) | (fileSeq[2] << 8) | fileSeq[3]);
            uint combined = fileSeqInt + addr;
            byte[] combinedBytes = new byte[4];
            combinedBytes[0] = (byte)(combined >> 24);
            combinedBytes[1] = (byte)(combined >> 16);
            combinedBytes[2] = (byte)(combined >> 8);
            combinedBytes[3] = (byte)combined;
            cmd.AddRange(combinedBytes);

            byte[] lenBytes = new byte[4];
            lenBytes[0] = (byte)(originalLength >> 24);
            lenBytes[1] = (byte)(originalLength >> 16);
            lenBytes[2] = (byte)(originalLength >> 8);
            lenBytes[3] = (byte)originalLength;
            cmd.AddRange(lenBytes);

            cmd.AddRange(compressedData);

            byte[] crc = Crc16X25.CalculateBytes(cmd.ToArray());
            cmd.AddRange(crc);

            byte[] converted = ConvertData(cmd.ToArray());

            List<byte> result = new List<byte> { 0x7E };
            result.AddRange(converted);
            result.Add(0x7E);

            return result.ToArray();
        }

        private byte[] CreateRebootCommand()
        {
            byte[] cmd = new byte[] { 0x0A };
            byte[] crc = Crc16X25.CalculateBytes(cmd);

            List<byte> result = new List<byte> { 0x7E };
            result.AddRange(cmd);
            result.AddRange(crc);
            result.Add(0x7E);

            return result.ToArray();
        }

        private byte[] CreateForceRebootCommand()
        {
            byte[] cmd = new byte[] { 0x32 };
            byte[] crc = Crc16X25.CalculateBytes(cmd);

            List<byte> result = new List<byte> { 0x7E };
            result.AddRange(cmd);
            result.AddRange(crc);
            result.Add(0x7E);

            return result.ToArray();
        }

        public void SendRebootCommands()
        {

            SerialPort port = null;
            
            while (port == null)
            {
                port = BuildConnection();
                if (port == null)
                {
                    var result = OnRetryRequired?.Invoke("Device Not Found", 
                        "No HiSilicon device found in USB Update Mode.\n\nConnect the device and click Done! to retry, or Cancel to abort.");
                    
                    if (result == false)
                    {
                        return;
                    }
                }
            }

            try
            {
                SendCommand(port, CreateRebootCommand(), 0.3);
                Thread.Sleep(50);
                SendCommand(port, CreateForceRebootCommand(), 0.3);
            }
            finally
            {
                port?.Close();
                port?.Dispose();
            }
        }
    }
}
