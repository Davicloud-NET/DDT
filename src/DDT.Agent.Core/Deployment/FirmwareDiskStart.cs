// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;

namespace DDT.Agent.Deployment;

// Names the disk's boot entry in BootNext, so a firmware that puts the network first starts the disk all the same. The
// entry is the one a hand-over put first for the disk's fallback loader, and is made again where the firmware lost it.
public sealed class FirmwareDiskStart(IUefiVariables variables, AgentLog log) : IDiskStart
{
    public const string BootNext = "BootNext";

    // What a boot entry is called that has to be made here, where nothing names the image any more.
    public const string Description = "Linux";

    public static string NoSystemPartitionMessage(LocalDisk disk)
    {
        ArgumentNullException.ThrowIfNull(disk);

        return $"Disk {disk.Number} has no EFI system partition, so the firmware cannot be told to start it next. The machine starts " +
            "it only if its firmware tries the disk.";
    }

    public void Next(LocalDisk disk, EspPartition? systemPartition, bool first)
    {
        ArgumentNullException.ThrowIfNull(disk);

        if (systemPartition is null)
        {
            log.Warning(NoSystemPartitionMessage(disk));

            return;
        }

        try
        {
            FirmwareBootEntry entries = new(variables, log);
            ushort entry = (first ? null : entries.Find(systemPartition, FirmwareBootEntry.FallbackLoaderPath))
                ?? entries.MakeFirst(systemPartition, FirmwareBootEntry.FallbackLoaderPath, Description, []);
            byte[] next = new byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(next, entry);
            variables.Write(BootNext, next);
            log.Information($"The next start comes from boot entry {FirmwareBootEntry.OptionName(entry)}, which starts disk {disk.Number}, whatever the boot order says.");
        }
        catch (Exception exception)
        {
            // Nothing may keep the machine from restarting, so the failure is said and not thrown on
            log.Warning(
                $"The firmware could not be told to start disk {disk.Number} next ({exception.Message}). If the machine starts from " +
                "the network again, the agent tries once more then.");
        }
    }
}
