// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using DDT.Contracts.Images;
using DDT.Core.Boot;
using Xunit;

namespace DDT.Agent.Tests;

// The server's tests hold Microsoft's certificates and check that they are found; here only what db can hold otherwise.
public sealed class SecureBootTrustTests
{
    [Fact]
    public void SaysNothingWithoutADatabaseOrForAMalformedOne()
    {
        Assert.Null(SecureBootTrust.From(null));
        Assert.Null(SecureBootTrust.From([1, 2, 3]));
    }

    [Fact]
    public void TrustsNotAnotherCertificate()
    {
        byte[] certificate = [.. Enumerable.Repeat((byte)0x30, 600)];
        byte[] database = new byte[28 + 16 + certificate.Length];
        SignatureDatabase.X509Type.TryWriteBytes(database);
        BinaryPrimitives.WriteUInt32LittleEndian(database.AsSpan(16), (uint)database.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(database.AsSpan(24), (uint)(16 + certificate.Length));
        certificate.CopyTo(database, 28 + 16);

        Assert.Equal(UefiCa.None, SecureBootTrust.From(database));
    }
}
