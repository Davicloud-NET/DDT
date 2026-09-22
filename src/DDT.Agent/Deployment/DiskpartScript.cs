// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.Text;

namespace DDT.Agent.Deployment;

// Microsoft's UEFI layout: EFI system, MSR, Windows, and a recovery partition right after Windows so that Windows
// can grow it later. 300 MB covers 4K native disks too, and 1 GB leaves WinRE its 250 MB free: the defaults, which a
// Partition step can change.
public static class DiskpartScript
{
    public const int SystemPartitionMegabytes = 300;
    public const int ReservedPartitionMegabytes = 16;
    public const int RecoveryPartitionMegabytes = 1024;

    private const string RecoveryPartitionType = "de94bba4-06d1-4d40-a16a-bfd50179d6ac";

    // Required and hidden: GPT_ATTRIBUTE_PLATFORM_REQUIRED and GPT_BASIC_DATA_ATTRIBUTE_NO_DRIVE_LETTER.
    private const string RecoveryAttributes = "0x8000000000000001";

    // diskpart reads ASCII with CRLF line ends and stops at a blank line.
    public static string Build(
        int diskNumber,
        char system,
        char windows,
        char recovery,
        int systemPartitionMegabytes = SystemPartitionMegabytes,
        int recoveryPartitionMegabytes = RecoveryPartitionMegabytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(diskNumber);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(systemPartitionMegabytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(recoveryPartitionMegabytes);

        string[] lines =
        [
            string.Create(CultureInfo.InvariantCulture, $"select disk {diskNumber}"),
            "clean",
            "convert gpt",
            string.Create(CultureInfo.InvariantCulture, $"create partition efi size={systemPartitionMegabytes}"),
            "format quick fs=fat32 label=\"System\"",
            $"assign letter={system}",
            string.Create(CultureInfo.InvariantCulture, $"create partition msr size={ReservedPartitionMegabytes}"),
            "create partition primary",
            string.Create(CultureInfo.InvariantCulture, $"shrink minimum={recoveryPartitionMegabytes}"),
            "format quick fs=ntfs label=\"Windows\"",
            $"assign letter={windows}",
            "create partition primary",
            "format quick fs=ntfs label=\"Recovery\"",
            $"assign letter={recovery}",
            $"set id=\"{RecoveryPartitionType}\"",
            $"gpt attributes={RecoveryAttributes}",
            "exit",
        ];

        return Script(lines);
    }

    // Gives the system and recovery partitions of a run letters again after a restart. The recovery partition keeps
    // its attributes: they only keep Windows from giving it a letter by itself.
    public static string AssignLetters(int diskNumber, uint systemPartition, char system, uint recoveryPartition, char recovery)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(diskNumber);

        return Script(
        [
            string.Create(CultureInfo.InvariantCulture, $"select disk {diskNumber}"),
            string.Create(CultureInfo.InvariantCulture, $"select partition {systemPartition}"),
            $"assign letter={system}",
            string.Create(CultureInfo.InvariantCulture, $"select partition {recoveryPartition}"),
            $"assign letter={recovery}",
            "exit",
        ]);
    }

    private static string Script(string[] lines)
    {
        StringBuilder script = new();

        foreach (string line in lines)
        {
            script.Append(line).Append("\r\n");
        }

        return script.ToString();
    }
}
