// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using DDT.Agent.Facts;
using Xunit;

namespace DDT.Agent.Tests;

// The Windows functions behind the facts, on the machine the tests run on. Its memory, cores and tables are whatever
// they are, so only what every Windows machine has is checked. This proves the declarations and the layouts the parsers
// assume.
public sealed class HardwareFactsTests
{
    [Fact]
    public void CountsThisMachinesMemory()
    {
        SystemHardware hardware = new();
        ulong? usable = hardware.UsableMemoryBytes();

        Assert.True(usable is > 0);

        // Some virtual machines list no memory devices, and then Windows cannot say what is installed.
        if (hardware.InstalledMemoryKilobytes() is { } installed)
        {
            Assert.True(installed * 1024 >= usable);
        }
    }

    [Fact]
    public void CountsThisMachinesCoresAndLogicalProcessors()
    {
        byte[]? records = new SystemHardware().ProcessorCores();

        Assert.NotNull(records);

        ProcessorCount? count = ProcessorCount.From(records);

        Assert.NotNull(count);
        Assert.InRange(count.Cores, 1, count.LogicalProcessors);
        Assert.InRange(Environment.ProcessorCount, 1, count.LogicalProcessors);
    }

    // Every ACPI machine has a FADT, whose signature is FACP. Reading it by the id its letters make proves the order.
    [Fact]
    public void ListsThisMachinesAcpiTablesByTheirSignatures()
    {
        FirmwareTables tables = new();
        byte[]? ids = tables.List(FirmwareTables.AcpiProvider);

        Assert.NotNull(ids);
        Assert.Equal(0, ids.Length % 4);

        byte[]? fadt = tables.Read(FirmwareTables.AcpiProvider, BinaryPrimitives.ReadUInt32LittleEndian("FACP"u8));

        Assert.NotNull(fadt);
        Assert.Equal("FACP"u8.ToArray(), fadt[..4]);
    }

    [Fact]
    public void ReadsThisMachinesSmbiosTable()
    {
        byte[]? raw = new FirmwareTables().Read(FirmwareTables.RawSmbiosProvider, 0);

        Assert.NotNull(raw);
        Assert.NotNull(SmbiosParser.TryReadSystemInformation(raw));
    }
}
