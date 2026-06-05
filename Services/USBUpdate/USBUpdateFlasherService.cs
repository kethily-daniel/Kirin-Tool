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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Kirin_Tool.Utils;

namespace Kirin_Tool.Services.USBUpdate
{
    public class USBUpdateFlasherService
    {
        private readonly Action<string> _log;
        private readonly string _dloadDirectory;
        private readonly Func<string, string, Task<bool>> _onRetryRequired;

        public Action<int>? OnProgressUpdate;
        public Action<List<(string PartitionName, string SourceLabel)>>? OnPartitionsDiscovered;
        public Action<int, string, int>? OnPartitionProgress;
        public Action<int, string, bool, string>? OnPartitionCompleted;
        public Action<string>? OnExtractionStarted;
        public Action<int>? OnExtractionProgress;

        public USBUpdateFlasherService(Action<string> log, string dloadDirectory, Func<string, string, Task<bool>> onRetryRequired)
        {
            _log = log;
            _dloadDirectory = dloadDirectory;
            _onRetryRequired = onRetryRequired;
        }

        public async Task FlashPartitions(IEnumerable<(string FilePath, string Label, List<string> SelectedPartitions)> filesData, CancellationToken cancellationToken = default)
        {
            if (Directory.Exists(_dloadDirectory))
            {
                try { Directory.Delete(_dloadDirectory, true); } catch { }
            }
            Directory.CreateDirectory(_dloadDirectory);

            var sw = Stopwatch.StartNew();
            List<(string partitionName, string sourceLabel)> allPartitions = new List<(string, string)>();
            List<string> mappingLines = new List<string>();
            bool unlockCodeExtracted = false;

            int fileIndex = 0;
            foreach (var data in filesData)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(data.FilePath) || !File.Exists(data.FilePath))
                {
                    fileIndex++;
                    continue;
                }

                string subDir = Path.Combine(_dloadDirectory, $"part{fileIndex}");
                Directory.CreateDirectory(subDir);

                var extractor = new USBUpdateApp(_log, subDir);
                OnExtractionStarted?.Invoke(data.Label);
                
                var extracted = extractor.ExtractAllPartitions(data.FilePath, 
                    extractUnlockCode: !unlockCodeExtracted,
                    onProgress: (p) => OnExtractionProgress?.Invoke(p),
                    includePartitions: data.SelectedPartitions);

                if (!unlockCodeExtracted)
                {
                    string unlockPath = Path.Combine(subDir, "unlockcode");
                    if (File.Exists(unlockPath))
                    {
                        File.Copy(unlockPath, Path.Combine(_dloadDirectory, "unlockcode"), true);
                        unlockCodeExtracted = true;
                    }
                }

                foreach (var line in extracted)
                {
                    var parts = line.Split(' ');
                    if (parts.Length >= 1)
                    {
                        allPartitions.Add((parts[0], data.Label));
                        mappingLines.Add($"{parts[0]}|{subDir}");
                    }
                }
                fileIndex++;
            }

            // Super partition merging - detect and merge multiple super partitions
            var superIndices = new List<int>();
            for (int i = 0; i < allPartitions.Count; i++)
            {
                if (allPartitions[i].partitionName.Equals("super", StringComparison.OrdinalIgnoreCase))
                {
                    superIndices.Add(i);
                }
            }

            if (superIndices.Count > 1)
            {
                OnExtractionStarted?.Invoke("Merging super partitions");
                OnExtractionProgress?.Invoke(0);

                // Collect the super .img paths from each source directory
                var superImgPaths = new List<string>();
                string firstSuperHeaderPath = null;

                foreach (var idx in superIndices)
                {
                    var mappingParts = mappingLines[idx].Split('|');
                    if (mappingParts.Length >= 2)
                    {
                        string sourceDir = mappingParts[1];
                        string imgPath = Path.Combine(sourceDir, "super.img");
                        if (File.Exists(imgPath))
                        {
                            superImgPaths.Add(imgPath);
                            if (firstSuperHeaderPath == null)
                            {
                                string headerPath = Path.Combine(sourceDir, "super.img.header");
                                if (File.Exists(headerPath))
                                    firstSuperHeaderPath = headerPath;
                            }
                        }
                    }
                }

                if (superImgPaths.Count > 1)
                {
                    string mergedDir = Path.Combine(_dloadDirectory, "super_merged");
                    Directory.CreateDirectory(mergedDir);
                    string mergedSuperPath = Path.Combine(mergedDir, "super.img");

                    string currentInput = superImgPaths[0];
                    for (int i = 1; i < superImgPaths.Count; i++)
                    {
                        string nextOutput = i == superImgPaths.Count - 1
                            ? mergedSuperPath
                            : Path.Combine(mergedDir, $"super_intermediate_{i}.img");

                        SuperMerger.MergeSuperImages(currentInput, superImgPaths[i], nextOutput, p =>
                        {
                            OnExtractionProgress?.Invoke((int)p);
                        }).GetAwaiter().GetResult();

                        if (i > 1 && currentInput.StartsWith(mergedDir) && File.Exists(currentInput))
                        {
                            try { File.Delete(currentInput); } catch { }
                        }

                        currentInput = nextOutput;
                    }

                    if (firstSuperHeaderPath != null)
                    {
                        string mergedHeaderPath = Path.Combine(mergedDir, "super.img.header");
                        File.Copy(firstSuperHeaderPath, mergedHeaderPath, true);

                        long mergedSize = new FileInfo(mergedSuperPath).Length;
                        byte[] headerBytes = File.ReadAllBytes(mergedHeaderPath);
                        if (headerBytes.Length >= 28)
                        {
                            headerBytes[24] = (byte)(mergedSize & 0xFF);
                            headerBytes[25] = (byte)((mergedSize >> 8) & 0xFF);
                            headerBytes[26] = (byte)((mergedSize >> 16) & 0xFF);
                            headerBytes[27] = (byte)((mergedSize >> 24) & 0xFF);
                            File.WriteAllBytes(mergedHeaderPath, headerBytes);
                        }
                    }

                    int firstSuperIndex = superIndices[0];
                    for (int i = superIndices.Count - 1; i >= 0; i--)
                    {
                        allPartitions.RemoveAt(superIndices[i]);
                        mappingLines.RemoveAt(superIndices[i]);
                    }

                    allPartitions.Insert(firstSuperIndex, ("super", "Merged Super"));
                    mappingLines.Insert(firstSuperIndex, $"super|{mergedDir}");

                    OnExtractionProgress?.Invoke(100);
                }
            }

            if (allPartitions.Count == 0)
            {
                throw new Exception("No partitions found to flash.");
            }

            OnPartitionsDiscovered?.Invoke(allPartitions);

            var listLines = allPartitions.Select(p => $"{p.partitionName} 1").ToList();
            File.WriteAllLines(Path.Combine(_dloadDirectory, "list.txt"), listLines);
            File.WriteAllLines(Path.Combine(_dloadDirectory, "partition_mapping.txt"), mappingLines);

            cancellationToken.ThrowIfCancellationRequested();

            var usbService = new UsbDownloadService(_log, _dloadDirectory);
            usbService.OnRetryRequired += (title, message) =>
            {
                return _onRetryRequired(title, message).GetAwaiter().GetResult();
            };

            usbService.OnProgressUpdate += (progress) => OnProgressUpdate?.Invoke(progress);
            usbService.OnPartitionStarted += (index, name) => OnPartitionProgress?.Invoke(index, name, 0);
            usbService.OnPartitionProgressUpdate += (index, name, prog) => OnPartitionProgress?.Invoke(index, name, prog);
            usbService.OnPartitionCompleted += (index, name, success, msg) => OnPartitionCompleted?.Invoke(index, name, success, msg);

            bool flashSuccess = false;
            try
            {
                flashSuccess = await Task.Run(() => usbService.FlashImages(), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new Exception($"USB Flashing failed: {ex.Message}", ex);
            }

            if (!flashSuccess)
            {
                throw new Exception("One or more partitions failed to flash.");
            }
        }
    }
}
