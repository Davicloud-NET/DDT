// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using DDT.Server.Applications.Installers;
using Xunit;

namespace DDT.Server.Tests.Applications;

public sealed class NsisUninstallKeyTests
{
    private static readonly (uint, string?)[] s_example = [(TestNsis.LocalMachine, TestNsis.Uninstall + "Example App")];

    [Theory]
    [InlineData(true, "lzma", true)]
    [InlineData(true, "lzma", false)]
    [InlineData(true, "deflate", true)]
    [InlineData(true, "deflate", false)]
    [InlineData(true, "none", false)]
    [InlineData(false, "lzma", true)]
    [InlineData(false, "deflate", false)]
    public void ALiteralKeyIsRead(bool unicode, string compression, bool solid)
    {
        Assert.Equal("Example App", KeyOf(TestNsis.Exe(TestNsis.Header(s_example, unicode), compression, solid)));
    }

    [Fact]
    public void TheSameKeyUnderEveryRootAndInAnyCaseIsOneKey()
    {
        (uint, string?)[] writes =
        [
            (TestNsis.LocalMachine, TestNsis.Uninstall + "Example App"),
            (TestNsis.CurrentUser, TestNsis.Uninstall + "Example App"),
            (TestNsis.ShellContext, TestNsis.Uninstall.ToUpperInvariant() + "EXAMPLE APP"),
            (TestNsis.LocalMachine | 0x20000000, TestNsis.Uninstall + "Example App"),
        ];

        Assert.Equal("Example App", KeyOf(TestNsis.Exe(TestNsis.Header(writes))));
    }

    [Theory]
    [InlineData("Example \u0003老")]
    [InlineData("\u0001耂")]
    [InlineData("\u0002考\\Example")]
    [InlineData("Example\\Sub")]
    [InlineData(" Example")]
    public void AKeyThatIsNoPlainTextGivesNone(string key)
    {
        Assert.Null(KeyOf(TestNsis.Exe(TestNsis.Header([(TestNsis.LocalMachine, TestNsis.Uninstall + key)]))));
    }

    [Fact]
    public void ALanguageStringGivesNone()
    {
        Assert.Null(KeyOf(TestNsis.Exe(TestNsis.Header([.. s_example, (TestNsis.LocalMachine, null)]))));
    }

    [Fact]
    public void TwoKeysGiveNone()
    {
        (uint, string?)[] writes = [.. s_example, (TestNsis.LocalMachine, TestNsis.Uninstall + "Example App Beta")];

        Assert.Null(KeyOf(TestNsis.Exe(TestNsis.Header(writes))));
    }

    [Fact]
    public void NoKeyGivesNone()
    {
        Assert.Null(KeyOf(TestNsis.Exe(TestNsis.Header([]))));
    }

    // A cut past the header may still leave all of it; a cut into it gives none.
    [Theory]
    [InlineData("lzma", true)]
    [InlineData("lzma", false)]
    [InlineData("deflate", true)]
    [InlineData("deflate", false)]
    public void ATruncatedInstallerGivesNoneOrTheKey(string compression, bool solid)
    {
        byte[] exe = TestNsis.Exe(TestNsis.Header(s_example), compression, solid);
        int overlay = TestExe.Create(new TestExeOptions()).Length;
        int half = overlay + ((exe.Length - overlay) / 2);

        for (int length = overlay; length < exe.Length; length++)
        {
            string? key = Probe(exe[..length]).UninstallKey;

            Assert.True(key is null || (key == "Example App" && length > half), $"{key} at {length}");
        }
    }

    [Fact]
    public void AHeaderLengthPastTheCapGivesNone()
    {
        byte[] overlay = TestNsis.Overlay(TestNsis.Header(s_example), "lzma", solid: true);
        BinaryPrimitives.WriteInt32LittleEndian(overlay.AsSpan(20), (16 * 1024 * 1024) + 1);

        Assert.Null(KeyOf(TestExe.Create(new TestExeOptions { Overlay = overlay })));
    }

    [Fact]
    public void ABlockLongerThanTheFileGivesNone()
    {
        byte[] overlay = TestNsis.Overlay(TestNsis.Header(s_example), "deflate", solid: false);
        BinaryPrimitives.WriteUInt32LittleEndian(overlay.AsSpan(28), 0xFFFFFFFF);

        Assert.Null(KeyOf(TestExe.Create(new TestExeOptions { Overlay = overlay })));
    }

    [Fact]
    public void BlockOffsetsAndCountsPastTheHeaderGiveNone()
    {
        foreach (int field in new[] { 4 + 16, 8 + 16, 4 + 24, 4 + 32 })
        {
            byte[] header = TestNsis.Header(s_example);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(field), int.MaxValue);

            Assert.Null(KeyOf(TestNsis.Exe(header)));
        }
    }

    [Fact]
    public void AStringWithoutAnEndGivesNone()
    {
        byte[] header = TestNsis.Header(s_example);
        int strings = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4 + 24));
        header.AsSpan(strings).Fill(0x41);

        Assert.Null(KeyOf(TestNsis.Exe(header, "none", solid: false)));
    }

    [Fact]
    public void RandomDamageNeverThrows()
    {
        byte[] original = TestNsis.Exe(TestNsis.Header(s_example), "none", solid: false);
        int overlay = TestExe.Create(new TestExeOptions()).Length;
        Random random = new(20261112);

        for (int round = 0; round < 2000; round++)
        {
            byte[] damaged = (byte[])original.Clone();
            damaged[overlay + random.Next(original.Length - overlay)] = (byte)random.Next(256);
            damaged[overlay + random.Next(original.Length - overlay)] = (byte)random.Next(256);

            _ = Probe(damaged);
        }
    }

    private static string? KeyOf(byte[] installer)
    {
        InstallerFacts facts = Probe(installer);
        Assert.Equal(InstallerKind.Nsis, facts.Kind);

        return facts.UninstallKey;
    }

    private static InstallerFacts Probe(byte[] installer)
    {
        using MemoryStream stream = new(installer);

        return InstallerProbe.Read(stream);
    }
}
