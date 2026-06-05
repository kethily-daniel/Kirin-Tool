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
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace Kirin_Tool.Services
{
    public class FastbootFlasherService
    {
        private readonly FastbootClient _fastbootClient;

        public FastbootFlasherService(FastbootClient fastbootClient)
        {
            _fastbootClient = fastbootClient;
        }

        public async Task<List<FastbootPartition>> GetPartitionTableAsync()
        {
            var result = await _fastbootClient.GetVarAsync("ptable");

            if (string.IsNullOrEmpty(result))
            {
                throw new InvalidOperationException("Failed to get partition table from device");
            }

            var partitions = new List<FastbootPartition>();
            var lines = result.Split(new char[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                var match = Regex.Match(line.Trim(), @"\(bootloader\)\s*:(.+)");
                if (match.Success)
                {
                    var partitionName = match.Groups[1].Value.Trim();
                    if (!string.IsNullOrEmpty(partitionName))
                    {
                        partitions.Add(new FastbootPartition(partitionName));
                    }
                }
            }

            return partitions;
        }

        public async Task<(bool IsSuccess, string Message)> DumpPartitionAsync(string partitionName, string savePath)
        {
            try
            {
                var result = await _fastbootClient.OemCommandAsync($"dump-emmc {partitionName} \"{savePath}\"", timeoutMinutes: 300);
                bool isSuccess = !string.IsNullOrEmpty(result) &&
                                !result.ToLower().Contains("fail") &&
                                !result.ToLower().Contains("error");

                if (isSuccess)
                    return (true, result ?? "Dump completed");

                var storageResult = await _fastbootClient.OemCommandAsync($"dump-storage {partitionName} \"{savePath}\"", timeoutMinutes: 300);
                bool storageSuccess = !string.IsNullOrEmpty(storageResult) &&
                                     !storageResult.ToLower().Contains("fail") &&
                                     !storageResult.ToLower().Contains("error");
                return (storageSuccess, storageResult ?? "Dump completed");
            }
            catch (TimeoutException)
            {
                return (false, "Dump operation timed out - partition may be too large");
            }
            catch (Exception ex)
            {
                return (false, $"Dump failed: {ex.Message}");
            }
        }


        public async Task<(bool IsSuccess, string Message)> FlashPartitionAsync(string partitionName, string imagePath)
        {
            try
            {
                var result = await _fastbootClient.FlashPartition(partitionName, imagePath);

                return (result.IsSuccess, result.Output);
            }
            catch (Exception ex)
            {
                // return (false, $"Flash failed: {ex.Message}");
                return (false, $"Failed");
            }
        }

        public void GenerateFlashingXml(List<FastbootPartition> partitions, string outputPath)
        {
            var doc = new XDocument(
                new XDeclaration("1.0", "gb2312", "yes"),
                new XElement("configurations",
                    new XElement("configuration",
                        new XElement("fastbootimage",
                            partitions.Select(p =>
                                new XElement("image",
                                    new XAttribute("name", p.Name.ToUpperInvariant()),
                                    new XAttribute("identifier", p.Identifier),
                                    $"fastbootimage/{p.ImageFileName}")
                            )
                        )
                    )
                )
            );

            doc.Save(outputPath);
        }

        public List<FastbootPartition> ParseFlashingXml(string xmlPath)
        {
            var partitions = new List<FastbootPartition>();

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            var settings = new XmlReaderSettings
            {
                IgnoreWhitespace = true,
                IgnoreComments = true
            };

            XDocument doc;
            using (var reader = XmlReader.Create(xmlPath, settings))
            {
                doc = XDocument.Load(reader);
            }

            var fastbootImageSection = doc.Descendants("fastbootimage").FirstOrDefault();
            if (fastbootImageSection == null)
                return partitions;

            var images = fastbootImageSection.Elements("image");

            foreach (var image in images)
            {
                var name = image.Attribute("name")?.Value;
                var identifier = image.Attribute("identifier")?.Value;
                var fileName = image.Value?.Trim();

                if (string.IsNullOrEmpty(fileName))
                    continue;

                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(identifier))
                {
                    if (identifier.Equals("huawei_crc_check", StringComparison.OrdinalIgnoreCase))
                        continue;
                    var partition = new FastbootPartition(identifier)
                    {
                        Name = name,
                        IsSelected = true
                    };

                    fileName = fileName.Replace('/', '\\');

                    var xmlDirectory = Path.GetDirectoryName(xmlPath);
                    var parentDirectory = Path.GetDirectoryName(xmlDirectory);

                    if (Path.IsPathRooted(fileName))
                    {
                        partition.DumpPath = fileName;
                    }
                    else
                    {
                        var candidatePath = Path.Combine(xmlDirectory, fileName);

                        if (!File.Exists(candidatePath) && !string.IsNullOrEmpty(parentDirectory))
                        {
                            var parentCandidate = Path.Combine(parentDirectory, fileName);
                            if (File.Exists(parentCandidate))
                            {
                                candidatePath = parentCandidate;
                            }
                        }

                        partition.DumpPath = candidatePath;
                    }

                    partitions.Add(partition);
                }
            }

            return partitions;
        }

    }
}
