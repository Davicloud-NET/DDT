// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using System.Formats.Asn1;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using DDT.Server.Images;

namespace DDT.Server.Tests;

// EFI programs for the Authenticode tests: the real ones under Fixtures/SecureBoot, and minimal PE32+ files signed here.
internal static class TestPe
{
    private const int PeOffset = 0x40;
    private const int OptionalHeader = PeOffset + 24;
    private const int CertificateEntry = OptionalHeader + 112 + (4 * 8);

    public static byte[] Fixture(string name)
    {
        using FileStream file = File.OpenRead(Path.Combine(Repository.Root(), "tests", "DDT.Server.Tests", "Fixtures", "SecureBoot", name + ".gz"));
        using GZipStream gzip = new(file, CompressionMode.Decompress);
        using MemoryStream content = new();
        gzip.CopyTo(content);

        return content.ToArray();
    }

    // Headers in the first 512 bytes, then one section of 512 bytes.
    public static byte[] Create(ushort machine = PeImage.MachineAmd64, int seed = 1)
    {
        byte[] file = new byte[1024];
        Span<byte> span = file;
        span[0] = (byte)'M';
        span[1] = (byte)'Z';
        BinaryPrimitives.WriteInt32LittleEndian(span[0x3C..], PeOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(span[PeOffset..], 0x00004550);
        BinaryPrimitives.WriteUInt16LittleEndian(span[(PeOffset + 4)..], machine);
        BinaryPrimitives.WriteUInt16LittleEndian(span[(PeOffset + 6)..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(span[(PeOffset + 20)..], 240);
        BinaryPrimitives.WriteUInt16LittleEndian(span[(PeOffset + 22)..], 0x22);
        BinaryPrimitives.WriteUInt16LittleEndian(span[OptionalHeader..], 0x20B);
        BinaryPrimitives.WriteInt32LittleEndian(span[(OptionalHeader + 60)..], 0x200);
        BinaryPrimitives.WriteUInt16LittleEndian(span[(OptionalHeader + 68)..], 10);
        BinaryPrimitives.WriteUInt32LittleEndian(span[(OptionalHeader + 108)..], 16);

        int section = OptionalHeader + 240;
        ".text"u8.CopyTo(span[section..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[(section + 8)..], 0x200);
        BinaryPrimitives.WriteInt32LittleEndian(span[(section + 12)..], 0x1000);
        BinaryPrimitives.WriteInt32LittleEndian(span[(section + 16)..], 0x200);
        BinaryPrimitives.WriteInt32LittleEndian(span[(section + 20)..], 0x200);
        new Random(seed).NextBytes(file.AsSpan(0x200));

        return file;
    }

    // Appends a WIN_CERTIFICATE with signer's Authenticode signature over the file's SHA-256 hash, carrying the
    // certificates in chain. Then it points the certificate table at all the entries.
    public static byte[] Sign(byte[] file, X509Certificate2 signer, params X509Certificate2[] chain)
    {
        PeImage image = PeImage.Read(file) ?? throw new ArgumentException("Not a PE file.", nameof(file));
        int tableOffset = image.CertificateTableLength == 0 ? Align(file.Length) : image.CertificateTableOffset;
        byte[] unsigned = file[..(image.CertificateTableLength == 0 ? file.Length : image.CertificateTableOffset)];
        byte[] existing = image.CertificateTableLength == 0 ? [] : file[image.CertificateTableOffset..];

        ContentInfo content = new(new Oid("1.3.6.1.4.1.311.2.1.4"), IndirectData(image.Hash(HashAlgorithmName.SHA256)));
        SignedCms cms = new(content, detached: false);
        CmsSigner cmsSigner = new(SubjectIdentifierType.IssuerAndSerialNumber, signer)
        {
            IncludeOption = X509IncludeOption.EndCertOnly,
            DigestAlgorithm = new Oid("2.16.840.1.101.3.4.2.1"),
        };
        cmsSigner.Certificates.AddRange(chain);
        cms.ComputeSignature(cmsSigner);
        byte[] pkcs7 = cms.Encode();

        byte[] entry = new byte[Align(8 + pkcs7.Length)];
        BinaryPrimitives.WriteInt32LittleEndian(entry, 8 + pkcs7.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(entry.AsSpan(4), 0x0200);
        BinaryPrimitives.WriteUInt16LittleEndian(entry.AsSpan(6), 0x0002);
        pkcs7.CopyTo(entry, 8);

        byte[] signed = new byte[tableOffset + existing.Length + entry.Length];
        unsigned.CopyTo(signed, 0);
        existing.CopyTo(signed, tableOffset);
        entry.CopyTo(signed, tableOffset + existing.Length);
        BinaryPrimitives.WriteInt32LittleEndian(signed.AsSpan(CertificateEntry), tableOffset);
        BinaryPrimitives.WriteInt32LittleEndian(signed.AsSpan(CertificateEntry + 4), existing.Length + entry.Length);

        return signed;
    }

    // An authority, or a certificate it issues, with its private key.
    public static X509Certificate2 Certificate(string name, X509Certificate2? issuer = null, bool authority = true, DateTimeOffset? notAfter = null)
    {
        using RSA key = RSA.Create(2048);
        CertificateRequest request = new($"CN={name}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(authority, false, 0, critical: true));
        DateTimeOffset notBefore = DateTimeOffset.UtcNow.AddYears(-2);
        DateTimeOffset end = notAfter ?? DateTimeOffset.UtcNow.AddYears(2);

        if (issuer is null)
        {
            return request.CreateSelfSigned(notBefore, end);
        }

        // An issued certificate cannot outlive its issuer.
        DateTimeOffset issuerEnd = new(issuer.NotAfter.ToUniversalTime(), TimeSpan.Zero);
        using X509Certificate2 issued = request.Create(issuer, notBefore, end < issuerEnd ? end : issuerEnd, RandomNumberGenerator.GetBytes(12));

        return issued.CopyWithPrivateKey(key);
    }

    private static int Align(int length) => (length + 7) & ~7;

    // SpcIndirectDataContent with SPC_PE_IMAGE_DATA as its data and the hash in a DigestInfo.
    private static byte[] IndirectData(byte[] sha256)
    {
        AsnWriter writer = new(AsnEncodingRules.DER);

        using (writer.PushSequence())
        {
            using (writer.PushSequence())
            {
                writer.WriteObjectIdentifier("1.3.6.1.4.1.311.2.1.15");

                using (writer.PushSequence())
                {
                    writer.WriteBitString([]);
                }
            }

            using (writer.PushSequence())
            {
                using (writer.PushSequence())
                {
                    writer.WriteObjectIdentifier("2.16.840.1.101.3.4.2.1");
                    writer.WriteNull();
                }

                writer.WriteOctetString(sha256);
            }
        }

        return writer.Encode();
    }
}
