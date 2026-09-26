// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
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

    private static NormalisedRegistration WithDisks(params AgentDisk[] disks)
    {
        AgentRegistration registration = new(Guid.NewGuid().ToString(), "00155D010203", ["00155D010203"], null, null, null, "1", null, disks);

        Assert.True(RegistrationValidator.TryNormalise(registration, out NormalisedRegistration? normalised, out _));

        return normalised!;
    }
}
