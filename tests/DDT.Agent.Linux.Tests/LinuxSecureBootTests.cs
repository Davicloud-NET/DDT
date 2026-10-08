// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using DDT.Agent.Deployment;
using DDT.Contracts.Images;
using DDT.Core.Boot;
using Xunit;

namespace DDT.Agent.Tests;

// Secure Boot from the firmware's own variables.
public sealed class LinuxSecureBootTests : IDisposable
{
    private readonly LinuxTree _tree = new();

    public void Dispose() => _tree.Dispose();

    private LinuxSecureBoot SecureBoot() => new(new LinuxUefiVariables(_tree.Root, _ => { }));

    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    public void ReadsTheStateFromTheFirmwaresVariable(byte value, bool enabled)
    {
        _tree.WriteVariable("SecureBoot", LinuxUefiVariables.Global, value);

        Assert.Equal(enabled, SecureBoot().Enabled());
    }

    // Firmware that cannot do Secure Boot has other variables and not this one.
    [Fact]
    public void FirmwareWithoutTheVariableHasSecureBootOff()
    {
        _tree.WriteVariable("BootOrder", LinuxUefiVariables.Global, 0, 0);

        Assert.False(SecureBoot().Enabled());
    }

    [Fact]
    public void CannotTellWhereTheVariablesAreNotShown()
    {
        Assert.Null(SecureBoot().Enabled());
        Assert.Null(SecureBoot().TrustedCas());

        Directory.CreateDirectory(_tree.Variables);

        Assert.Null(SecureBoot().Enabled());
        Assert.Null(SecureBoot().TrustedCas());
    }

    // db is read under the image security database's GUID, and its attributes are no part of the signature lists.
    [Fact]
    public void ReadsWhatTheFirmwareTrustsFromItsSignatureDatabase()
    {
        byte[] certificate = [.. Enumerable.Repeat((byte)0x30, 600)];
        byte[] database = new byte[28 + 16 + certificate.Length];
        SignatureDatabase.X509Type.TryWriteBytes(database);
        BinaryPrimitives.WriteUInt32LittleEndian(database.AsSpan(16), (uint)database.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(database.AsSpan(24), (uint)(16 + certificate.Length));
        certificate.CopyTo(database, 28 + 16);
        string file = _tree.WriteVariable("db", LinuxUefiVariables.ImageSecurityDatabase, database);

        Assert.Equal("db-d719b2cb-3d3a-4596-a3bc-dad00e67656f", Path.GetFileName(file));
        Assert.Equal(UefiCa.None, SecureBoot().TrustedCas());

        // With the attributes left in, the first list would not be one.
        Assert.Null(SecureBootTrust.From([0x07, 0x00, 0x00, 0x00, .. database]));
    }
}
