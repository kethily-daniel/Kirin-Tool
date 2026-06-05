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
using System.Buffers;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Kirin_Tool.Utils
{
    public static class SuperMerger
    {
        public static async Task MergeSuperImages(string path1, string path2, string outputPath, Action<double> progressCallback = null)
        {
            await Task.Run(() => PerformMerge(path1, path2, outputPath, progressCallback));
        }

        private static void PerformMerge(string path1, string path2, string outputPath, Action<double> progressCallback)
        {
            var len1 = new FileInfo(path1).Length;
            var len2 = new FileInfo(path2).Length;

            string largePath = path1, smallPath = path2;
            long largeLen = len1, smallLen = len2;

            if (len2 > len1)
            {
                largePath = path2;
                smallPath = path1;
                largeLen = len2;
                smallLen = len1;
            }

            long totalSize = largeLen + smallLen;
            const int bufferSize = 4 * 1024 * 1024;
            var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);

            try
            {
                long lastChunkOffset = 0;
                SparseHeader hLarge, hSmall;

                using (var fs = new FileStream(largePath, FileMode.Open, FileAccess.Read))
                using (var reader = new BinaryReader(fs))
                {
                    hLarge = ReadStruct<SparseHeader>(reader);
                    if (hLarge.Magic != SparseConstants.SPARSE_HEADER_MAGIC) 
                        throw new Exception($"File {Path.GetFileName(largePath)} is not a valid sparse image.");

                    for (int i = 0; i < hLarge.TotalChunks; i++)
                    {
                        lastChunkOffset = fs.Position;
                        var chunk = ReadStruct<ChunkHeader>(reader);
                        fs.Seek(chunk.TotalSize - SparseConstants.CHUNK_HEADER_SIZE, SeekOrigin.Current);
                    }
                }

                using (var src = new FileStream(largePath, FileMode.Open, FileAccess.Read))
                using (var dst = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
                {
                    long remaining = lastChunkOffset;
                    while (remaining > 0)
                    {
                        int toRead = (int)Math.Min(bufferSize, remaining);
                        int read = src.Read(buffer, 0, toRead);
                        if (read == 0) break;
                        dst.Write(buffer, 0, read);
                        remaining -= read;
                        progressCallback?.Invoke((double)dst.Position * 100 / totalSize);
                    }
                }

                long newDataOffset = 0;
                using (var fs = new FileStream(smallPath, FileMode.Open, FileAccess.Read))
                using (var reader = new BinaryReader(fs))
                {
                    hSmall = ReadStruct<SparseHeader>(reader);
                    if (hSmall.Magic != SparseConstants.SPARSE_HEADER_MAGIC) 
                        throw new Exception($"File {Path.GetFileName(smallPath)} is not a valid sparse image.");

                    var firstChunk = ReadStruct<ChunkHeader>(reader);
                    newDataOffset = fs.Position + (firstChunk.TotalSize - SparseConstants.CHUNK_HEADER_SIZE);
                }

                using (var src = new FileStream(smallPath, FileMode.Open, FileAccess.Read))
                using (var dst = new FileStream(outputPath, FileMode.Append, FileAccess.Write))
                {
                    src.Seek(newDataOffset, SeekOrigin.Begin);
                    int read;
                    while ((read = src.Read(buffer, 0, bufferSize)) > 0)
                    {
                        dst.Write(buffer, 0, read);
                        progressCallback?.Invoke((double)dst.Position * 100 / totalSize);
                    }
                }

                uint newTotalChunks = (hLarge.TotalChunks - 1) + (hSmall.TotalChunks - 1);
                using (var fs = new FileStream(outputPath, FileMode.Open, FileAccess.Write))
                using (var writer = new BinaryWriter(fs))
                {
                    fs.Seek(0x0C, SeekOrigin.Begin);
                    writer.Write(4096); 
                    
                    fs.Seek(0x14, SeekOrigin.Begin);
                    writer.Write(newTotalChunks);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        private static T ReadStruct<T>(BinaryReader reader) where T : struct
        {
            byte[] bytes = reader.ReadBytes(Marshal.SizeOf<T>());
            GCHandle handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try { return Marshal.PtrToStructure<T>(handle.AddrOfPinnedObject()); }
            finally { handle.Free(); }
        }
    }
}
