// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using DDT.Agent.Deployment;
using Xunit;

namespace DDT.Agent.Tests;

// The firmware is told to start a disk next, whatever its boot order says, by the entry a hand-over made for the disk.
public sealed class FirmwareDiskStartTests
{
    private static readonly LocalDisk s_disk = FakeDeploymentTools.Disk(0);

    private static readonly EspPartition s_esp = new(1, 2048, 4096, Guid.Parse("5e2b1a3c-4d6f-4a8b-9c0d-1e2f3a4b5c6d"));

    private static readonly EspPartition s_otherEsp = s_esp with { PartitionId = Guid.Parse("0f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0") };

    private readonly FakeUefiVariables _variables = new();
    private readonly StringWriter _console = new();
    private readonly AgentLog _log;

    public FirmwareDiskStartTests()
    {
        _log = new AgentLog(new ImmediateTimeProvider(), _console);

        // As a firmware that puts the network first again, with the image's own entry before the one the hand-over made.
        _variables.Values["Boot0000"] = EfiLoadOption.Build("EFI Network", s_otherEsp, @"\EFI\Boot\bootx64.efi");
        _variables.Values["Boot0002"] = EfiLoadOption.Build("ubuntu", s_esp, @"\EFI\ubuntu\shimx64.efi");
        _variables.Values["Boot0006"] = EfiLoadOption.Build("noble-test", s_esp, FirmwareBootEntry.FallbackLoaderPath);
        _variables.Values["BootOrder"] = Order(0, 2, 6);
    }

    private static byte[] Order(params ushort[] numbers)
    {
        byte[] order = new byte[numbers.Length * 2];

        for (int index = 0; index < numbers.Length; index++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(order.AsSpan(index * 2), numbers[index]);
        }

        return order;
    }

    // The installed Linux has the run and may have put its own entry first, so the order is its to keep.
    [Fact]
    public void NamesTheDisksEntryAsTheNextStartAndLeavesTheBootOrderAlone()
    {
        new FirmwareDiskStart(_variables, _log).Next(s_disk, s_esp, first: false);

        Assert.Equal(Order(6), _variables.Values["BootNext"]);
        Assert.Equal(Order(0, 2, 6), _variables.Values["BootOrder"]);
        Assert.Equal(["BootNext"], _variables.Writes);
        Assert.Contains("The next start comes from boot entry Boot0006, which starts disk 0, whatever the boot order says.", _console.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void PutsTheDiskFirstAgainAndNamesItAsTheNextStart()
    {
        new FirmwareDiskStart(_variables, _log).Next(s_disk, s_esp, first: true);

        Assert.Equal(Order(6), _variables.Values["BootNext"]);
        Assert.Equal(Order(6, 0, 2), _variables.Values["BootOrder"]);
        Assert.Equal(["BootOrder", "BootNext"], _variables.Writes);

        // The partition is not new: the machine has had it since the image was written.
        Assert.Contains(
            "INFO  Firmware boot entry Boot0006 already starts Linux on the EFI system partition of disk 0. It is now first in the boot order.",
            _console.ToString(),
            StringComparison.Ordinal);
        Assert.DoesNotContain("new EFI system partition", _console.ToString(), StringComparison.Ordinal);
    }

    // A firmware may drop an entry it could not start, or one made for a one-time boot menu.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MakesTheEntryAgainWhereTheFirmwareLostIt(bool first)
    {
        _variables.Values.Remove("Boot0006");
        _variables.Values["BootOrder"] = Order(0, 2);

        new FirmwareDiskStart(_variables, _log).Next(s_disk, s_esp, first);

        Assert.Equal(EfiLoadOption.Build(FirmwareDiskStart.Description, s_esp, FirmwareBootEntry.FallbackLoaderPath), _variables.Values["Boot0001"]);
        Assert.Equal(Order(1, 0, 2), _variables.Values["BootOrder"]);
        Assert.Equal(Order(1), _variables.Values["BootNext"]);
        Assert.Contains(
            "INFO  Added firmware boot entry Boot0001 for Linux on the EFI system partition of disk 0 and put it first in the boot order.",
            _console.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ADiskWithoutAnEfiSystemPartitionIsSaidAndTheFirmwareIsNotTouched()
    {
        new FirmwareDiskStart(_variables, _log).Next(s_disk, null, first: true);

        Assert.Empty(_variables.Writes);
        Assert.Contains(
            "WARN  Disk 0 has no EFI system partition, so the firmware cannot be told to start it next. The machine starts it only if its firmware tries the disk.",
            _console.ToString(),
            StringComparison.Ordinal);
    }

    // Nothing here may keep the machine from restarting.
    [Fact]
    public void AFirmwareThatRefusesIsSaidAndNothingIsThrown()
    {
        _variables.Failure = new DeploymentStepException("The firmware variables are read-only.");

        new FirmwareDiskStart(_variables, _log).Next(s_disk, s_esp, first: false);

        Assert.Contains(
            "WARN  The firmware could not be told to start disk 0 next (The firmware variables are read-only.). If the machine starts from " +
            "the network again, the agent tries once more then.",
            _console.ToString(),
            StringComparison.Ordinal);
    }
}
