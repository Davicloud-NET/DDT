// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using System.Globalization;

namespace DDT.Agent.Deployment;

// Puts a firmware boot entry for a loader on the new EFI system partition first in BootOrder: Windows Boot Manager
// after a Windows image, the fallback file after a raw disk image. An entry is recognised by the partition and file it
// starts, never by its description: one left by an earlier installation carries the same description but points at a
// partition that no longer exists. diskpart gives every new partition a new GUID, so an entry for an EFI system
// partition the deployment erased, whatever it started, is reused rather than left behind, dead.
public static class FirmwareBootEntry
{
    public const string WindowsDescription = "Windows Boot Manager";
    public const string WindowsLoaderPath = @"\EFI\Microsoft\Boot\bootmgfw.efi";

    // What firmware starts from a disk without an entry of its own, and what a raw disk image is started from.
    public const string FallbackLoaderPath = @"\EFI\BOOT\BOOTX64.EFI";

    private const string BootOrder = "BootOrder";

    public static void MakeFirst(
        IUefiVariables variables,
        EspPartition esp,
        string loaderPath,
        string description,
        IReadOnlyCollection<Guid> erasedPartitionIds,
        AgentLog log)
    {
        ArgumentNullException.ThrowIfNull(variables);
        ArgumentNullException.ThrowIfNull(esp);
        ArgumentException.ThrowIfNullOrEmpty(loaderPath);
        ArgumentException.ThrowIfNullOrEmpty(description);
        ArgumentNullException.ThrowIfNull(erasedPartitionIds);
        ArgumentNullException.ThrowIfNull(log);

        List<ushort> order = ReadOrder(variables.Read(BootOrder));
        ushort? existing = null;
        ushort? erased = null;

        foreach (ushort number in order)
        {
            if (variables.Read(OptionName(number)) is not { } option)
            {
                continue;
            }

            if (EfiLoadOption.PointsAt(option, esp.PartitionId, loaderPath))
            {
                existing = number;

                break;
            }

            if (erased is null && erasedPartitionIds.Any(id => EfiLoadOption.PointsAt(option, id, path: null)))
            {
                erased = number;
            }
        }

        ushort entry;

        if (existing is { } found)
        {
            entry = found;
        }
        else if (erased is { } reused)
        {
            variables.Write(OptionName(reused), EfiLoadOption.Build(description, esp, loaderPath));
            entry = reused;
        }
        else
        {
            entry = Create(variables, esp, loaderPath, description);
        }

        List<ushort> first = [entry, .. order.Where(number => number != entry).Distinct()];
        bool moved = !first.SequenceEqual(order);

        if (moved)
        {
            variables.Write(BootOrder, WriteOrder(first));
        }

        string name = OptionName(entry);

        if (existing is not null)
        {
            log.Information(moved
                ? $"Firmware boot entry {name} already starts {description} on the new EFI system partition. It is now first in the boot order."
                : $"Firmware boot entry {name} already starts {description} on the new EFI system partition and is first in the boot order.");
        }
        else if (erased is not null)
        {
            log.Information(
                $"Firmware boot entry {name} pointed at the EFI system partition this deployment erased. It now starts {description} on " +
                "the new EFI system partition and is first in the boot order.");
        }
        else
        {
            log.Information($"Added firmware boot entry {name} for {description} on the new EFI system partition and put it first in the boot order.");
        }
    }

    private static ushort Create(IUefiVariables variables, EspPartition esp, string loaderPath, string description)
    {
        for (int number = 0; number <= ushort.MaxValue; number++)
        {
            string name = OptionName((ushort)number);

            if (variables.Read(name) is null)
            {
                variables.Write(name, EfiLoadOption.Build(description, esp, loaderPath));

                return (ushort)number;
            }
        }

        throw new DeploymentStepException($"Every firmware boot entry number is in use, so no entry for {description} can be added.");
    }

    private static string OptionName(ushort number) => string.Create(CultureInfo.InvariantCulture, $"Boot{number:X4}");

    // An array of UINT16; a stray last byte is ignored.
    private static List<ushort> ReadOrder(byte[]? value)
    {
        List<ushort> order = [];

        for (int offset = 0; value is not null && offset + 1 < value.Length; offset += 2)
        {
            order.Add(BinaryPrimitives.ReadUInt16LittleEndian(value.AsSpan(offset)));
        }

        return order;
    }

    private static byte[] WriteOrder(List<ushort> order)
    {
        byte[] value = new byte[order.Count * 2];

        for (int index = 0; index < order.Count; index++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(value.AsSpan(index * 2), order[index]);
        }

        return value;
    }
}
