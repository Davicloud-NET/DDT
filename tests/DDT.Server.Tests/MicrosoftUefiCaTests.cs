// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using DDT.Contracts.Images;
using DDT.Core.Boot;
using DDT.Server.Images;
using Xunit;

namespace DDT.Server.Tests;

// The agent recognises Microsoft's third-party UEFI CAs in a machine's db by thumbprints that Core keeps.
// They have to be the certificates the server checks boot files against.
public sealed class MicrosoftUefiCaTests
{
    private static byte[] X509List(params X509Certificate2[] certificates)
    {
        int signatureSize = 16 + certificates[0].RawData.Length;
        byte[] list = new byte[28 + signatureSize];
        SignatureDatabase.X509Type.TryWriteBytes(list);
        BinaryPrimitives.WriteUInt32LittleEndian(list.AsSpan(16), (uint)list.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(list.AsSpan(24), (uint)signatureSize);
        certificates[0].RawData.CopyTo(list, 28 + 16);

        return list;
    }

    [Fact]
    public void KnowsTheCertificatesTheServerChecksAgainst()
    {
        Assert.Equal(
            [MicrosoftUefiCa.Thumbprint2011, MicrosoftUefiCa.Thumbprint2023],
            UefiCertificateAuthorities.Microsoft.Select(certificate => Convert.ToHexStringLower(SHA256.HashData(certificate.RawData))).Order());
    }

    [Fact]
    public void FindsEitherInADatabase()
    {
        Assert.Equal(
            [UefiCa.Microsoft2011, UefiCa.Microsoft2023],
            UefiCertificateAuthorities.Microsoft.Select(authority => MicrosoftUefiCa.TrustedBy(X509List(authority))).Order());

        using X509Certificate2 other = TestPe.Certificate("Microsoft Windows Production PCA 2011");
        Assert.Equal(UefiCa.None, MicrosoftUefiCa.TrustedBy(X509List(other)));
    }
}
