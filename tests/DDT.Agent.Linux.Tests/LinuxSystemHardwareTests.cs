// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Facts;
using Xunit;

namespace DDT.Agent.Tests;

// Memory and processors from /sys and /proc, and the memory modules of the SMBIOS table.
public sealed class LinuxSystemHardwareTests : IDisposable
{
    private readonly LinuxTree _tree = new();

    public void Dispose() => _tree.Dispose();

    // A Memory Device of SMBIOS 2.7, 34 bytes, with its size at 0x0C and its extended size at 0x1C, and no strings.
    private static byte[] MemoryDevice(ushort size, uint extendedSize = 0)
    {
        byte[] device = new byte[34 + 2];
        device[0] = 17;
        device[1] = 34;
        BitConverter.TryWriteBytes(device.AsSpan(0x0C), size);
        BitConverter.TryWriteBytes(device.AsSpan(0x1C), extendedSize);

        return device;
    }

    private void AddProcessor(int number, int package, int core)
    {
        _tree.Write($"{package}\n", "sys", "devices", "system", "cpu", $"cpu{number}", "topology", "physical_package_id");
        _tree.Write($"{core}\n", "sys", "devices", "system", "cpu", $"cpu{number}", "topology", "core_id");
    }

    // A module in megabytes, one in kilobytes, one of 32 GB in the extended field, an empty socket and an unknown size.
    [Fact]
    public void AddsUpTheMemoryModulesOfTheMachine()
    {
        byte[] table =
        [
            .. MemoryDevice(8192),
            .. MemoryDevice(0x8000 | 4096),
            .. MemoryDevice(0x7FFF, 32768),
            .. MemoryDevice(0),
            .. MemoryDevice(0xFFFF),
            127, 4, 0, 0, 0, 0,
        ];

        Assert.Equal((8192UL * 1024) + 4096 + (32768UL * 1024), SmbiosMemoryDevices.TotalKilobytes(table));
    }

    [Fact]
    public void KeepsWhatItCountedBeforeAStructureThatIsCutShort()
    {
        byte[] table = [.. MemoryDevice(2048), 17, 34, 0, 0, 1, 2, 3];

        Assert.Equal(2048UL * 1024, SmbiosMemoryDevices.TotalKilobytes(table));
        Assert.Equal(0UL, SmbiosMemoryDevices.TotalKilobytes([]));
        Assert.Equal(0UL, SmbiosMemoryDevices.TotalKilobytes([17, 2, 0, 0]));
    }

    [Fact]
    public void ReadsTheInstalledMemoryFromTheFirmwaresTableAndTheUsableFromTheKernel()
    {
        _tree.Write([.. MemoryDevice(2048), 127, 4, 0, 0, 0, 0], "sys", "firmware", "dmi", "tables", "DMI");
        _tree.Write("MemFree:          100 kB\nMemTotal:        2014836 kB\nMemAvailable:    1800000 kB\n", "proc", "meminfo");
        LinuxSystemHardware hardware = new(_tree.Root);

        Assert.Equal(2048UL * 1024, hardware.InstalledMemoryKilobytes());
        Assert.Equal(2014836UL * 1024, hardware.UsableMemoryBytes());
    }

    // Two cores with two threads each: every logical processor has a folder, and a core is counted once.
    [Fact]
    public void CountsCoresAndLogicalProcessorsFromTheirTopology()
    {
        AddProcessor(0, 0, 0);
        AddProcessor(1, 0, 0);
        AddProcessor(2, 0, 1);
        AddProcessor(3, 0, 1);
        AddProcessor(10, 1, 0);
        _tree.Write("performance\n", "sys", "devices", "system", "cpu", "cpufreq", "policy0", "scaling_governor");
        _tree.Write("0\n", "sys", "devices", "system", "cpu", "cpu4", "online");

        Assert.Equal(new ProcessorCount(3, 5), new LinuxSystemHardware(_tree.Root).ProcessorCores());
    }

    [Fact]
    public void SaysNothingOfWhatTheSystemDoesNotShow()
    {
        _tree.Write("no modules here"u8.ToArray(), "sys", "firmware", "dmi", "tables", "DMI");
        _tree.Write("MemTotal: many kB\n", "proc", "meminfo");
        LinuxSystemHardware bare = new(_tree.PathOf("nothing"));
        LinuxSystemHardware odd = new(_tree.Root);

        Assert.Null(bare.InstalledMemoryKilobytes());
        Assert.Null(bare.UsableMemoryBytes());
        Assert.Null(bare.ProcessorCores());
        Assert.Null(odd.InstalledMemoryKilobytes());
        Assert.Null(odd.UsableMemoryBytes());
    }
}
