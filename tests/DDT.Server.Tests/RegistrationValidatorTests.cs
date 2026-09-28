// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Machines;
using DDT.Server.Machines;
using Xunit;

namespace DDT.Server.Tests;

public sealed class RegistrationValidatorTests
{
    [Fact]
    public void NormalisesMacsAndUuidAndPutsThePrimaryInTheList()
    {
        AgentRegistration registration = new(
            "44454C4C-5700-1038-8036-B7C04F5A344A",
            "00:15:5d:01:02:03",
            ["00-15-5D-01-02-03", "00155D010204", "00155d010204"],
            "  Dell Inc.  ",
            "",
            null,
            "1.0.0");

        Assert.True(RegistrationValidator.TryNormalise(registration, out NormalisedRegistration? normalised, out _));

        Assert.Equal("44454c4c-5700-1038-8036-b7c04f5a344a", normalised!.SmbiosUuid);
        Assert.Equal("00155D010203", normalised.PrimaryMac);
        Assert.Equal(["00155D010203", "00155D010204"], normalised.MacAddresses);
        Assert.Equal("Dell Inc.", normalised.Manufacturer);
        Assert.Null(normalised.Model);
    }

    [Fact]
    public void RefusesAPrimaryMacThatIsNotAmongTheAddresses()
    {
        AgentRegistration registration = new(Guid.NewGuid().ToString(), "00155D010203", ["00155D010204"], null, null, null, "1");

        Assert.False(RegistrationValidator.TryNormalise(registration, out _, out string error));
        Assert.Contains("primaryMac", error, StringComparison.Ordinal);
    }

    [Fact]
    public void BoundsFreeText()
    {
        AgentRegistration registration = new(Guid.NewGuid().ToString(), "00155D010203", ["00155D010203"], new string('m', 500), null, null, new string('v', 100));

        Assert.True(RegistrationValidator.TryNormalise(registration, out NormalisedRegistration? normalised, out _));
        Assert.Equal(128, normalised!.Manufacturer!.Length);
        Assert.Equal(32, normalised.AgentVersion.Length);
    }

    // Whoever booted boot.wim can send anything, but a chassis type has seven bits.
    [Theory]
    [InlineData(null, null)]
    [InlineData(0, 0)]
    [InlineData(10, 10)]
    [InlineData(127, 127)]
    [InlineData(128, null)]
    [InlineData(-1, null)]
    [InlineData(int.MaxValue, null)]
    public void KeepsOnlyAChassisTypeSmbiosCanHold(int? sent, int? kept)
    {
        AgentRegistration registration = new(Guid.NewGuid().ToString(), "00155D010203", ["00155D010203"], null, null, null, "1", ChassisType: sent);

        Assert.True(RegistrationValidator.TryNormalise(registration, out NormalisedRegistration? normalised, out _));
        Assert.Equal(kept, normalised!.ChassisType);
    }

    [Fact]
    public void DescribesOneDiskPerLine()
    {
        NormalisedRegistration normalised = WithDisks(
            new AgentDisk(0, "Msft Virtual Disk", 64L * 1024 * 1024 * 1024, "SCSI", 0),
            new AgentDisk(1, null, 2L * 1024 * 1024 * 1024 * 1024, "NVMe", 3));

        Assert.Equal("Disk 0: Msft Virtual Disk, 64 GB, SCSI\nDisk 1: unknown model, 2 TB, NVMe", normalised.Disks);
        Assert.Equal(2, normalised.EligibleDiskCount);
    }

    [Fact]
    public void BoundsAndCleansWhatTheAgentSaysAboutItsDisks()
    {
        NormalisedRegistration normalised = WithDisks(
            new AgentDisk(0, "Samsung\0 SSD\n" + new string('x', 100), 500_107_862_016, "NVMe\0" + new string('b', 40), 1));

        string line = Assert.Single(normalised.Disks!.Split('\n'));

        Assert.DoesNotContain('\0', line);
        Assert.StartsWith("Disk 0: Samsung SSD" + new string('x', 53) + ", 466 GB, NVMe", line, StringComparison.Ordinal);
        Assert.EndsWith(", NVMe" + new string('b', 12), line, StringComparison.Ordinal);
    }

    [Fact]
    public void KeepsWholeLinesWithinTheColumnButCountsEveryDisk()
    {
        AgentDisk[] disks = [.. Enumerable.Range(0, 24).Select(n => new AgentDisk(n, new string('m', 64), 1L << 40, "SAS", 0))];

        NormalisedRegistration normalised = WithDisks(disks);

        Assert.True(normalised.Disks!.Length <= 512);
        Assert.All(normalised.Disks.Split('\n'), line => Assert.EndsWith(", 1 TB, SAS", line, StringComparison.Ordinal));
        Assert.Equal(24, normalised.EligibleDiskCount);
    }

    [Fact]
    public void AnOldAgentReportsNoDisksAndANewOneMayReportNone()
    {
        AgentRegistration old = new(Guid.NewGuid().ToString(), "00155D010203", ["00155D010203"], null, null, null, "1");

        Assert.True(RegistrationValidator.TryNormalise(old, out NormalisedRegistration? unknown, out _));
        Assert.Null(unknown!.Disks);
        Assert.Null(unknown.EligibleDiskCount);

        NormalisedRegistration none = WithDisks();

        Assert.Null(none.Disks);
        Assert.Equal(0, none.EligibleDiskCount);
    }

    [Fact]
    public void AnAgentOlderThanVersion3SequencesSendsNoFacts()
    {
        Assert.Null(WithFacts(null).Facts);
    }

    [Fact]
    public void KeepsFactsThatCanBeRight()
    {
        MachineFacts facts = new()
        {
            MemoryMegabytes = 16384,
            ProcessorName = "13th Gen Intel(R) Core(TM) i7-1365U",
            ProcessorCores = 10,
            LogicalProcessors = 12,
            TpmPresent = true,
            TpmVersion = "2.0",
            SecureBootCapable = true,
            IPv4Address = "10.0.0.23",
            IPv4PrefixLength = 24,
            DefaultGateway = "10.0.0.1",
            DnsSuffix = "corp.example.com",
            DhcpServer = "10.0.0.2",
            SystemVersion = "ThinkPad T14 Gen 4",
            SystemFamily = "ThinkPad T14 Gen 4",
            SystemSku = "LENOVO_MT_21HD_BU_Think_FM_ThinkPad T14 Gen 4",
            AssetTag = "No Asset Information",
            BaseboardProduct = "21HDCTO1WW",
            BiosVersion = "R2FET53W (1.33 )",
            BiosDate = "2024-03-12",
        };

        Assert.Equal(facts, WithFacts(facts).Facts);
    }

    // Whoever booted boot.wim can send anything.
    // A fact that can't be right reads as unknown instead of refusing the machine.
    [Fact]
    public void BoundsAndCleansTheFacts()
    {
        MachineFacts facts = new()
        {
            MemoryMegabytes = -1,
            ProcessorName = "  Intel\0 Xeon\n" + new string('x', 200),
            ProcessorCores = 0,
            LogicalProcessors = 1 << 20,
            TpmVersion = "banana",
            IPv4Address = "10.1",
            IPv4PrefixLength = 33,
            DefaultGateway = "fe80::1",
            DnsSuffix = new string('d', 300),
            DhcpServer = "010.0.0.1",
            SystemVersion = "   ",
            AssetTag = "Tag \ud800 1",
            BiosDate = "03/12/2024",
        };

        MachineFacts normalised = WithFacts(facts).Facts!;

        Assert.Null(normalised.MemoryMegabytes);
        Assert.Equal("Intel Xeon" + new string('x', 118), normalised.ProcessorName);
        Assert.Null(normalised.ProcessorCores);
        Assert.Null(normalised.LogicalProcessors);
        Assert.Null(normalised.TpmVersion);
        Assert.Null(normalised.IPv4Address);
        Assert.Null(normalised.IPv4PrefixLength);
        Assert.Null(normalised.DefaultGateway);
        Assert.Equal(253, normalised.DnsSuffix!.Length);
        Assert.Null(normalised.DhcpServer);
        Assert.Null(normalised.SystemVersion);
        Assert.Equal("Tag  1", normalised.AssetTag);
        Assert.Null(normalised.BiosDate);
    }

    // A Gigabyte Z790 board leaves the system's version and SKU and the enclosure's asset tag as "Default string".
    // AMI firmware fills others with "To be filled by O.E.M.".
    // None of them may match a condition as if it were the machine's own value.
    [Fact]
    public void DropsThePlaceholdersABoardMakerLeftInTheFacts()
    {
        MachineFacts facts = new()
        {
            ProcessorName = "To Be Filled By O.E.M.",
            SystemVersion = "Default string",
            SystemFamily = "Z790 AORUS ELITE AX",
            SystemSku = "Default string",
            AssetTag = " default   STRING ",
            BaseboardProduct = "Z790 AORUS ELITE AX",
            BiosVersion = "System Version",
            DnsSuffix = "corp.example.com",
        };

        MachineFacts normalised = WithFacts(facts).Facts!;

        Assert.Equal(
            new MachineFacts
            {
                SystemFamily = "Z790 AORUS ELITE AX",
                BaseboardProduct = "Z790 AORUS ELITE AX",
                DnsSuffix = "corp.example.com",
            },
            normalised);
    }

    private static NormalisedRegistration WithFacts(MachineFacts? facts)
    {
        AgentRegistration registration = new(Guid.NewGuid().ToString(), "00155D010203", ["00155D010203"], null, null, null, "1", Facts: facts);

        Assert.True(RegistrationValidator.TryNormalise(registration, out NormalisedRegistration? normalised, out _));

        return normalised!;
    }

    private static NormalisedRegistration WithDisks(params AgentDisk[] disks)
    {
        AgentRegistration registration = new(Guid.NewGuid().ToString(), "00155D010203", ["00155D010203"], null, null, null, "1", null, disks);

        Assert.True(RegistrationValidator.TryNormalise(registration, out NormalisedRegistration? normalised, out _));

        return normalised!;
    }
}
