// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;
using DDT.Server.Applications.Installers;
using Xunit;

namespace DDT.Server.Tests.Applications;

public sealed class InstallerProbeTests
{
    private static readonly (string, string)[] s_version =
    [
        ("CompanyName", "Example Ltd"),
        ("FileDescription", "Example App Setup"),
        ("ProductName", "Example App"),
        ("ProductVersion", "7.8.9"),
    ];

    [Fact]
    public void AnMsiGivesItsProperties()
    {
        InstallerFacts facts = Probe(TestMsi.Fixture());

        Assert.Equal(new InstallerFacts(InstallerKind.Msi, "DDT Prüfpaket Größe", "Davicloud", "1.2.3", TestMsi.ProductCode, TestMsi.UpgradeCode), facts);
    }

    [Fact]
    public void ABurnBundleIsKnownByItsSectionAndGivesItsBundleCode()
    {
        Guid bundle = Guid.Parse("8f3c1a2b-4d5e-4f60-9a7b-0c1d2e3f4a5b");
        TestExeOptions options = new() { VersionStrings = s_version, BurnSection = TestExe.BurnHeader(bundle), Overlay = new byte[1000] };

        InstallerFacts facts = Probe(TestExe.Create(options));

        Assert.Equal(new InstallerFacts(InstallerKind.Burn, "Example App", "Example Ltd", "7.8.9", BundleCode: "{8F3C1A2B-4D5E-4F60-9A7B-0C1D2E3F4A5B}"), facts);
    }

    [Fact]
    public void AWixBurnSectionWithoutTheMagicIsNoBundle()
    {
        TestExeOptions options = new() { BurnSection = new byte[64] };

        Assert.Equal(InstallerKind.Unknown, Probe(TestExe.Create(options)).Kind);
    }

    [Fact]
    public void AnNsisInstallerIsKnownByTheFirstHeaderInItsOverlay()
    {
        byte[] overlay = [0, 0, 0, 0, 0xEF, 0xBE, 0xAD, 0xDE, .. "NullsoftInst"u8, .. new byte[200]];

        Assert.Equal(InstallerKind.Nsis, Probe(TestExe.Create(new TestExeOptions { VersionStrings = s_version, Overlay = overlay })).Kind);
    }

    [Fact]
    public void AnInnoSetupInstallerIsKnownByTheLoadersOffsetTable()
    {
        byte[] offsetTable = [.. "rDlPtS"u8, 0xCD, 0xE6, 0xD7, 0x7B, 0x0B, 0x2A, .. new byte[40]];
        TestExeOptions options = new() { VersionStrings = s_version, MoreResources = offsetTable, Overlay = new byte[3000] };

        Assert.Equal(InstallerKind.InnoSetup, Probe(TestExe.Create(options)).Kind);
    }

    [Fact]
    public void AnInstallShieldSetupIsKnownByItsSetupStream()
    {
        TestExeOptions options = new() { Overlay = [.. "ISSetupStream"u8, .. new byte[500]] };

        Assert.Equal(InstallerKind.InstallShield, Probe(TestExe.Create(options)).Kind);
    }

    [Fact]
    public void AnInstallShieldSetupIsKnownByItsVersionResource()
    {
        TestExeOptions options = new() { VersionStrings = [.. s_version, ("ISInternalVersion", "29.0.204")] };

        Assert.Equal(InstallerKind.InstallShield, Probe(TestExe.Create(options)).Kind);
    }

    [Fact]
    public void AnExeWithoutMarksIsUnknownButKeepsItsVersionResource()
    {
        InstallerFacts facts = Probe(TestExe.Create(new TestExeOptions { VersionStrings = s_version, Overlay = new byte[700] }));

        Assert.Equal(new InstallerFacts(InstallerKind.Unknown, "Example App", "Example Ltd", "7.8.9"), facts);
    }

    [Fact]
    public void WithoutVersionStringsTheFixedVersionAndTheDescriptionAreUsed()
    {
        TestExeOptions options = new() { VersionStrings = [("FileDescription", "Tool Setup")], FixedVersion = new Version(2, 4, 6, 8) };

        InstallerFacts facts = Probe(TestExe.Create(options));

        Assert.Equal(("Tool Setup", "2.4.6.8"), (facts.Name, facts.Version));
    }

    [Fact]
    public void MarksPastTheScannedWindowAreNotSought()
    {
        byte[] overlay = [.. new byte[128 * 1024], 0xEF, 0xBE, 0xAD, 0xDE, .. "NullsoftInst"u8];

        Assert.Equal(InstallerKind.Unknown, Probe(TestExe.Create(new TestExeOptions { Overlay = overlay })).Kind);
    }

    [Fact]
    public void ATruncatedExeIsUnknownAtAnyLength()
    {
        byte[] exe = TestExe.Create(new TestExeOptions { VersionStrings = s_version, BurnSection = TestExe.BurnHeader(Guid.NewGuid()) });

        for (int length = 0; length < 0x400; length += 7)
        {
            Assert.Equal(InstallerKind.Unknown, Probe(exe[..length]).Kind);
        }
    }

    [Fact]
    public void RandomDamageToAnExeNeverThrows()
    {
        byte[] original = TestExe.Create(new TestExeOptions { VersionStrings = s_version, BurnSection = TestExe.BurnHeader(Guid.NewGuid()) });
        Random random = new(20261111);

        for (int round = 0; round < 3000; round++)
        {
            byte[] damaged = (byte[])original.Clone();
            damaged[random.Next(0x700)] = (byte)random.Next(256);
            damaged[random.Next(0x700)] = (byte)random.Next(256);

            _ = Probe(damaged);
        }
    }

    [Fact]
    public void AFileThatIsNeitherIsUnknown()
    {
        Assert.Equal(InstallerFacts.Unknown, Probe(Encoding.ASCII.GetBytes("#!/bin/sh\necho hello\n")));
        Assert.Equal(InstallerFacts.Unknown, Probe([]));
    }

    private static InstallerFacts Probe(byte[] installer)
    {
        using MemoryStream stream = new(installer);

        return InstallerProbe.Read(stream);
    }
}
