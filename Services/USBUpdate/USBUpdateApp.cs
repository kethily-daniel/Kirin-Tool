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
using System.IO;
using System.Text;
using System.Windows;
using Kirin_Tool.Models;

namespace Kirin_Tool.Services.USBUpdate
{
    public class USBUpdateApp
    {
        private readonly Action<string> _log;
        private readonly string _dloadDirectory;

        public USBUpdateApp(Action<string> log, string dloadDirectory)
        {
            _log = log;
            _dloadDirectory = dloadDirectory;
        }

        public void ExtractUpToXloader(string updateAppPath)
        {
            Directory.CreateDirectory(_dloadDirectory);

            using (FileStream fs = new FileStream(updateAppPath, FileMode.Open, FileAccess.Read))
            using (BinaryReader reader = new BinaryReader(fs))
            {
                int startAddr = 0;
                byte[] unlockCmd = FindUnlockCode(reader, ref startAddr);

                File.WriteAllBytes(Path.Combine(_dloadDirectory, "unlockcode"), unlockCmd);

                fs.Seek(startAddr, SeekOrigin.Begin);

                List<string> imageList = new List<string>();
                bool foundXloader = false;

                while (true)
                {
                    if (fs.Position + 4 > fs.Length)
                        break;

                    var (dataLength, partitionName, headerData) = ParseImageHeader(reader);

                    if (dataLength == 0 || string.IsNullOrEmpty(partitionName))
                        break;


                    string headerPath = Path.Combine(_dloadDirectory, $"{partitionName}.img.header");
                    File.WriteAllBytes(headerPath, headerData);

                    string imgPath = Path.Combine(_dloadDirectory, $"{partitionName}.img");
                    ExtractImageData(reader, imgPath, dataLength);

                    if (File.Exists(imgPath))
                    {
                        long fileSize = new FileInfo(imgPath).Length;
                        if (fileSize == 0)
                        {
                            try
                            {
                                File.Delete(imgPath);
                                File.Delete(headerPath);
                            }
                            catch { }
                            continue;
                        }
                    }

                    imageList.Add($"{partitionName} 1");

                    if (partitionName.Equals("XLOADER", StringComparison.OrdinalIgnoreCase))
                    {
                        foundXloader = true;
                        break;
                    }

                    long currentPos = fs.Position;
                    int padding = (int)(4 - (currentPos % 4)) % 4;
                    if (padding > 0)
                        fs.Seek(padding, SeekOrigin.Current);
                }

                if (!foundXloader)
                {
                    throw new Exception("XLOADER partition not found in UPDATE.APP");
                }

                string listPath = Path.Combine(_dloadDirectory, "list.txt");
                File.WriteAllLines(listPath, imageList);
            }
        }

        public void ExtractSinglePartition(string updateAppPath, string targetPartition)
        {
            Directory.CreateDirectory(_dloadDirectory);

            using (FileStream fs = new FileStream(updateAppPath, FileMode.Open, FileAccess.Read))
            using (BinaryReader reader = new BinaryReader(fs))
            {
                int startAddr = 0;
                FindUnlockCode(reader, ref startAddr);
                fs.Seek(startAddr, SeekOrigin.Begin);

                while (true)
                {
                    if (fs.Position + 4 > fs.Length)
                        break;

                    var (dataLength, partitionName, headerData) = ParseImageHeader(reader);

                    if (dataLength == 0)
                        break;

                    if (partitionName.Equals(targetPartition, StringComparison.OrdinalIgnoreCase))
                    {
                        File.WriteAllBytes(Path.Combine(_dloadDirectory, $"{partitionName}.img.header"), headerData);

                        string imgPath = Path.Combine(_dloadDirectory, $"{partitionName}.img");
                        ExtractImageData(reader, imgPath, dataLength);
                        return;
                    }
                    else
                    {
                        long remaining = dataLength;
                        while (remaining > 0)
                        {
                            long toSkip = Math.Min(int.MaxValue, remaining);
                            fs.Seek(toSkip, SeekOrigin.Current);
                            remaining -= toSkip;
                        }
                    }

                    long currentPos = fs.Position;
                    int padding = (int)(4 - (currentPos % 4)) % 4;
                    if (padding > 0)
                        fs.Seek(padding, SeekOrigin.Current);
                }

                throw new Exception($"Partition {targetPartition} not found in package.");
            }
        }

        public (List<string> imageList, int startAddr) GetPartitionNames(string updateAppPath)
        {
            List<string> imageList = new List<string>();
            int foundAddr = 0;

            using (FileStream fs = new FileStream(updateAppPath, FileMode.Open, FileAccess.Read))
            using (BinaryReader reader = new BinaryReader(fs))
            {
                FindUnlockCode(reader, ref foundAddr);
                fs.Seek(foundAddr, SeekOrigin.Begin);

                while (true)
                {
                    if (fs.Position + 4 > fs.Length)
                        break;

                    var (dataLength, partitionName, _) = ParseImageHeader(reader);

                    if (dataLength == 0 || string.IsNullOrEmpty(partitionName))
                        break;

                    imageList.Add(partitionName);

                    long remaining = dataLength;
                    while (remaining > 0)
                    {
                        long toSkip = Math.Min(int.MaxValue, remaining);
                        fs.Seek(toSkip, SeekOrigin.Current);
                        remaining -= toSkip;
                    }

                    long currentPos = fs.Position;
                    int padding = (int)(4 - (currentPos % 4)) % 4;
                    if (padding > 0)
                        fs.Seek(padding, SeekOrigin.Current);
                }
            }

            return (imageList, foundAddr);
        }

        public List<string> ExtractAllPartitions(string updateAppPath, bool extractUnlockCode = false, int startAddr = -1, Action<int>? onProgress = null, List<string>? includePartitions = null)
        {
            Directory.CreateDirectory(_dloadDirectory);

            List<string> imageList = new List<string>();

            using (FileStream fs = new FileStream(updateAppPath, FileMode.Open, FileAccess.Read))
            using (BinaryReader reader = new BinaryReader(fs))
            {
                if (startAddr == -1)
                {
                    int foundAddr = 0;
                    byte[] unlockCmd = FindUnlockCode(reader, ref foundAddr);
                    startAddr = foundAddr;

                    if (extractUnlockCode)
                    {
                        string unlockPath = Path.Combine(_dloadDirectory, "unlockcode");
                        if (!File.Exists(unlockPath))
                        {
                            File.WriteAllBytes(unlockPath, unlockCmd);
                        }
                    }
                }

                fs.Seek(startAddr, SeekOrigin.Begin);

                while (true)
                {
                    if (fs.Position + 4 > fs.Length)
                        break;

                    var (dataLength, partitionName, headerData) = ParseImageHeader(reader);

                    if (dataLength == 0 || string.IsNullOrEmpty(partitionName))
                        break;

                    if (includePartitions != null && !includePartitions.Contains(partitionName, StringComparer.OrdinalIgnoreCase))
                    {
                        long remaining = dataLength;
                        while (remaining > 0)
                        {
                            long toSkip = Math.Min(int.MaxValue, remaining);
                            fs.Seek(toSkip, SeekOrigin.Current);
                            remaining -= toSkip;
                        }

                        long curr = fs.Position;
                        int pad = (int)(4 - (curr % 4)) % 4;
                        if (pad > 0)
                            fs.Seek(pad, SeekOrigin.Current);

                        continue;
                    }


                    string headerPath = Path.Combine(_dloadDirectory, $"{partitionName}.img.header");
                    File.WriteAllBytes(headerPath, headerData);

                    string imgPath = Path.Combine(_dloadDirectory, $"{partitionName}.img");
                    ExtractImageData(reader, imgPath, dataLength);

                    if (File.Exists(imgPath))
                    {
                        long fileSize = new FileInfo(imgPath).Length;
                        if (fileSize == 0)
                        {
                            try
                            {
                                File.Delete(imgPath);
                                File.Delete(headerPath);
                            }
                            catch { }
                            continue;
                        }
                    }

                    imageList.Add($"{partitionName} 1");
                    onProgress?.Invoke((int)((double)fs.Position / fs.Length * 100));

                    long currentPos = fs.Position;
                    int padding = (int)(4 - (currentPos % 4)) % 4;
                    if (padding > 0)
                        fs.Seek(padding, SeekOrigin.Current);
                }
            }

            return imageList;
        }

        private byte[] FindUnlockCode(BinaryReader reader, ref int startAddr)
        {
            long length = reader.BaseStream.Length;
            int bufferSize = 64 * 1024;
            byte[] buffer = new byte[bufferSize + 4];
            long currentPos = reader.BaseStream.Position;

            while (currentPos < length - 4)
            {
                reader.BaseStream.Seek(currentPos, SeekOrigin.Begin);
                int bytesRead = reader.BaseStream.Read(buffer, 0, buffer.Length);
                if (bytesRead < 4) break;

                for (int i = 0; i <= bytesRead - 4; i++)
                {
                    if (buffer[i] == 0x55 && buffer[i + 1] == 0xAA && buffer[i + 2] == 0x5A && buffer[i + 3] == 0xA5)
                    {
                        startAddr = (int)(currentPos + i);
                        reader.BaseStream.Seek(startAddr + 12, SeekOrigin.Begin);
                        byte[] unlockCode = reader.ReadBytes(8);
                        
                        string unlockStr = Encoding.ASCII.GetString(unlockCode).ToLower();
                        if (unlockStr.Contains("hw"))
                        {
                            return unlockCode;
                        }
                        
                        reader.BaseStream.Seek(startAddr + 1, SeekOrigin.Begin);
                    }
                }
                currentPos += (bytesRead - 3);
            }

            throw new Exception("Invalid UPDATE.APP file format: Magic not found");
        }

        private (long dataLength, string partitionName, byte[] headerData) ParseImageHeader(BinaryReader reader)
        {
            MemoryStream headerStream = new MemoryStream();
            BinaryWriter headerWriter = new BinaryWriter(headerStream);

            byte[] magic = reader.ReadBytes(4);

            if (magic.Length < 4)
                return (0, "", new byte[0]);

            headerWriter.Write(magic);

            if (magic[0] != 0x55 || magic[1] != 0xAA || magic[2] != 0x5A || magic[3] != 0xA5)
                return (0, "", new byte[0]);

            int headerLength = reader.ReadInt32();
            headerWriter.Write(headerLength);

            headerWriter.Write(reader.ReadBytes(4));
            headerWriter.Write(reader.ReadBytes(8));
            headerWriter.Write(reader.ReadBytes(4));

            uint dataLengthUint = reader.ReadUInt32();
            long dataLength = dataLengthUint;
            headerWriter.Write(dataLengthUint);

            headerWriter.Write(reader.ReadBytes(16));
            headerWriter.Write(reader.ReadBytes(16));

            byte[] nameBytes = reader.ReadBytes(32);
            headerWriter.Write(nameBytes);

            string partitionName = Encoding.UTF8.GetString(nameBytes).TrimEnd('\0');

            headerWriter.Write(reader.ReadBytes(6));

            int remainingHeaderLen = headerLength - 98;

            if (remainingHeaderLen > 0)
            {
                headerWriter.Write(reader.ReadBytes(remainingHeaderLen));
            }

            return (dataLength, partitionName, headerStream.ToArray());
        }

        private void ExtractImageData(BinaryReader reader, string outputPath, long dataLength)
        {
            using (FileStream outFile = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
            {
                long remaining = dataLength;
                int bufferSize = 1024 * 1024;
                byte[] buffer = new byte[bufferSize];

                while (remaining > 0)
                {
                    int toRead = (int)Math.Min(bufferSize, remaining);
                    int bytesRead = reader.Read(buffer, 0, toRead);
                    if (bytesRead == 0) break;
                    outFile.Write(buffer, 0, bytesRead);
                    remaining -= bytesRead;
                }
            }
        }
    }
}
