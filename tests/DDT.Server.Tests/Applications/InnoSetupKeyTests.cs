// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using DDT.Core;
using DDT.Server.Applications.Installers;
using Xunit;

namespace DDT.Server.Tests.Applications;

public sealed class InnoSetupKeyTests
{
    private const string Version = "6.7.0";

    // Where the first chunk's data starts in setup-0.bin of 6.7.0: ID, encryption header, block header, chunk CRC.
    private const int FirstChunkData = 64 + TestInno.EncryptionHeaderLength + 13 + 4;

    // The offset table and the encryption header both changed in 6.5.
    [Theory]
    [InlineData("5.5.7", false, false, false, 24)]
    [InlineData("5.5.7) (u", true, false, false, 24)]
    [InlineData("6.1.0) (u", true, false, false, 24)]
    [InlineData("6.4.0.1", true, false, false, 24)]
    [InlineData("6.5.0", true, true, false, 24)]
    [InlineData("6.7.0", true, true, true, 24)]
    [InlineData("7.0.0.3", true, true, true, 24)]
    [InlineData("7.1.1", true, true, true, 23)]
    public void AKnownVersionGivesTheAppIdsUninstallKey(string version, bool unicode, bool encryptionHeader, bool longSize, int createKeyIndex)
    {
        byte[] header = TestInno.Header(TestInno.Strings("Example App", createKeyIndex: createKeyIndex), unicode);

        Assert.Equal("Example App_is1", KeyOf(TestInno.SetupData(version, header, encryptionHeader, longSize), encryptionHeader ? 2 : 1));
    }

    [Fact]
    public void ADoubledBraceIsABrace()
    {
        Assert.Equal("{3F2A1B4C-5D6E-4F70-8192-A3B4C5D6E7F8}_is1", KeyOf("{{3F2A1B4C-5D6E-4F70-8192-A3B4C5D6E7F8}"));
    }

    [Theory]
    [InlineData("{code:GetAppId}")]
    [InlineData("{param:Id|Example}")]
    [InlineData("{#AppId}")]
    [InlineData("Example {app}")]
    [InlineData("Example {")]
    [InlineData("")]
    [InlineData("An AppId far longer than the fifty-seven characters Setup keeps")]
    public void AnAppIdOnlySetupCanExpandGivesNoKey(string appId)
    {
        Assert.Null(KeyOf(appId));
    }

    [Theory]
    [InlineData("not IsPortable", "yes")]
    [InlineData("yes", "no")]
    [InlineData("no", "yes")]
    public void ASetupThatMayNotWriteTheKeyGivesNone(string createKey, string uninstallable)
    {
        byte[] header = TestInno.Header(TestInno.Strings("Example App", createKey, uninstallable));

        Assert.Null(KeyOf(TestInno.SetupData(Version, header)));
    }

    [Fact]
    public void AnAnsiAppIdPastAsciiGivesNone()
    {
        byte[] header = TestInno.Header(TestInno.Strings("Exämple"), unicode: false);

        Assert.Null(KeyOf(TestInno.SetupData("5.5.7", header, encryptionHeader: false, longSize: false), 1));
    }

    [Theory]
    [InlineData("9.9.9")]
    [InlineData("6.7.0) (u")]
    [InlineData("5.5.8) (u")]
    public void AnUnknownVersionGivesNone(string version)
    {
        Assert.Null(KeyOf(TestInno.SetupData(version, TestInno.Header(TestInno.Strings("Example App")))));
    }

    [Fact]
    public void AFullyEncryptedHeaderGivesNone()
    {
        Assert.Null(KeyOf(TestInno.SetupData(Version, TestInno.Header(TestInno.Strings("Example App")), encryption: 2)));
    }

    [Fact]
    public void EncryptedFilesAloneLeaveTheHeaderReadable()
    {
        Assert.Equal("Example App_is1", KeyOf(TestInno.SetupData(Version, TestInno.Header(TestInno.Strings("Example App")), encryption: 1)));
    }

    [Fact]
    public void AHeaderOverSeveralChunksIsRead()
    {
        string[] strings = TestInno.Strings("Example App");
        Random random = new(7);
        strings[3] = new string([.. Enumerable.Range(0, 12000).Select(_ => (char)random.Next(0x4E00, 0x9FFF))]);

        Assert.Equal("Example App_is1", KeyOf(TestInno.SetupData(Version, TestInno.Header(strings))));
    }

    [Theory]
    [InlineData(64 + 4)]
    [InlineData(64 + TestInno.EncryptionHeaderLength + 5)]
    [InlineData(FirstChunkData + 10)]
    public void AChecksumThatDoesNotMatchGivesNone(int offset)
    {
        byte[] setupData = TestInno.SetupData(Version, TestInno.Header(TestInno.Strings("Example App")));
        setupData[offset] ^= 0x40;

        Assert.Null(KeyOf(setupData));
    }

    [Fact]
    public void AnOffsetTableWhoseChecksumDoesNotMatchGivesNone()
    {
        byte[] setupData = TestInno.SetupData(Version, TestInno.Header(TestInno.Strings("Example App")));
        int offset0 = TestExe.Create(new TestExeOptions { InnoOffsetTable = TestInno.OffsetTable(0, 2) }).Length;
        byte[] table = TestInno.OffsetTable(offset0, 2);
        table[60] ^= 1;

        Assert.Null(Probe(TestExe.Create(new TestExeOptions { InnoOffsetTable = table, Overlay = setupData })).UninstallKey);
    }

    [Fact]
    public void ATruncatedSetupGivesNoneAtAnyLength()
    {
        byte[] exe = TestInno.Exe(TestInno.SetupData(Version, TestInno.Header(TestInno.Strings("Example App"))));
        int overlay = TestExe.Create(new TestExeOptions { InnoOffsetTable = TestInno.OffsetTable(0, 2) }).Length;

        for (int length = overlay; length < exe.Length - 100; length += 3)
        {
            Assert.Null(Probe(exe[..length]).UninstallKey);
        }
    }

    [Fact]
    public void OversizedLengthFieldsGiveNone()
    {
        byte[] header = TestInno.Header(TestInno.Strings("Example App"));
        BinaryPrimitives.WriteInt32LittleEndian(header, int.MaxValue);
        byte[] setupData = TestInno.SetupData(Version, header);
        Assert.Null(KeyOf(setupData));

        setupData = TestInno.SetupData(Version, TestInno.Header(TestInno.Strings("Example App")));
        Span<byte> block = setupData.AsSpan(64 + TestInno.EncryptionHeaderLength, 13);
        BinaryPrimitives.WriteInt64LittleEndian(block[4..], long.MaxValue);
        BinaryPrimitives.WriteUInt32LittleEndian(block, Crc32.Append(0, block[4..]));
        Assert.Null(KeyOf(setupData));
    }

    [Fact]
    public void RandomDamageNeverThrows()
    {
        byte[] original = TestInno.Exe(TestInno.SetupData(Version, TestInno.Header(TestInno.Strings("Example App"))));
        int overlay = TestExe.Create(new TestExeOptions { InnoOffsetTable = TestInno.OffsetTable(0, 2) }).Length;
        Random random = new(20261112);

        for (int round = 0; round < 2000; round++)
        {
            byte[] damaged = (byte[])original.Clone();
            damaged[overlay + random.Next(original.Length - overlay)] = (byte)random.Next(256);

            _ = Probe(damaged);
        }
    }

    private static string? KeyOf(string appId) => KeyOf(TestInno.SetupData(Version, TestInno.Header(TestInno.Strings(appId))));

    private static string? KeyOf(byte[] setupData, int table = 2)
    {
        InstallerFacts facts = Probe(TestInno.Exe(setupData, table));
        Assert.Equal(InstallerKind.InnoSetup, facts.Kind);

        return facts.UninstallKey;
    }

    private static InstallerFacts Probe(byte[] installer)
    {
        using MemoryStream stream = new(installer);

        return InstallerProbe.Read(stream);
    }
}
