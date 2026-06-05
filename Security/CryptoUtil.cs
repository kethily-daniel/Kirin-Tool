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

using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Encodings;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Utilities.Encoders;
using System.IO;
using System.Text;

namespace Kirin_Tool.Security
{
    public static class CryptoUtil
    {
        public static byte[] DTL(byte[] efd)
        {
            using (var reader = new BinaryReader(new MemoryStream(efd)))
            {
                ushort eklen = (ushort)((reader.ReadByte() << 8) | reader.ReadByte());
                byte[] eaek = reader.ReadBytes(eklen);
                byte[] iv = reader.ReadBytes(16);
                byte[] edta = reader.ReadBytes((int)(reader.BaseStream.Length - reader.BaseStream.Position));


                var re = new OaepEncoding(new RsaEngine(), new Sha256Digest(), new Sha256Digest(), null);

                using (var stringReader = new StringReader(Encoding.UTF8.GetString(Convert.FromBase64String(Encoding.UTF8.GetString(Convert.FromBase64String(Encoding.UTF8.GetString(Convert.FromBase64String(Encoding.UTF8.GetString(Convert.FromBase64String(Constants.FBK))))))))))
                {
                    var pr = new PemReader(stringReader);
                    var pkp = (AsymmetricKeyParameter)pr.ReadObject();

                    re.Init(false, pkp);
                }

                byte[] aek = re.ProcessBlock(eaek, 0, eaek.Length);

                var cph = CipherUtilities.GetCipher(Encoding.UTF8.GetString(Convert.FromBase64String(Encoding.UTF8.GetString(Convert.FromBase64String("UVVWVEwwTkNReTlRUzBOVE4xQmhaR1JwYm1jPQ==")))));
                cph.Init(false, new ParametersWithIV(new KeyParameter(aek), iv));

                return cph.DoFinal(edta);
            }
        }
    }
}