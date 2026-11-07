// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using System.Globalization;

namespace DDT.Agent.Deployment;

// Puts a firmware boot entry for a loader on the new EFI system partition first in BootOrder. An entry is recognised by
// the partition and file it starts, never by its description. A dead entry from an earlier installation has the same
// description.
public sealed class FirmwareBootEntry(IUefiVariables variables, AgentLog log)
{
    public const string WindowsDescription = "Windows Boot Manager";
    public const string WindowsLoaderPath = @"\EFI\Microsoft\Boot\bootmgfw.efi";

    // What firmware starts from a disk that has no boot entry, and what a raw disk image is started from.
    public const string FallbackLoaderPath = @"\EFI\BOOT\BOOTX64.EFI";

    private const string BootOrder = "BootOrder";

    // Takes every other entry for an erased EFI system partition out of the firmware. Off, they stay, as in Windows PE.
    public bool RemovesDeadEntries { get; init; }

    // diskpart gives every new partition a new GUID. So an entry for an EFI system partition the deployment erased is
    // reused rather than left behind as a dead entry.
    public void MakeFirst(EspPartition esp, string loaderPath, string description, IReadOnlyCollection<Guid> erasedPartitionIds)
    {
        ArgumentNullException.ThrowIfNull(esp);
        ArgumentException.ThrowIfNullOrEmpty(loaderPath);
        ArgumentException.ThrowIfNullOrEmpty(description);
        ArgumentNullException.ThrowIfNull(erasedPartitionIds);

        List<ushort> order = ReadOrder(variables.Read(BootOrder));
        (ushort? existing, ushort? erased) = FindEntries(order, esp, loaderPath, erasedPartitionIds);
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
            entry = Create(esp, loaderPath, description);
        }

        List<ushort> dead = RemovesDeadEntries ? [.. DeadEntries(order, erasedPartitionIds).Where(number => number != entry)] : [];
        List<ushort> first = [entry, .. order.Where(number => number != entry && !dead.Contains(number)).Distinct()];
        bool moved = !first.SequenceEqual(order);

        if (moved)
        {
            variables.Write(BootOrder, WriteOrder(first));
        }

        Report(OptionName(entry), description, existing is not null, erased is not null, moved);

        // After the order no longer names them, so the firmware never finds an entry there that is gone.
        foreach (ushort number in dead)
        {
            Remove(OptionName(number));
        }
    }

    // The loader is first by now, so an entry the firmware keeps is only said.
    private void Remove(string name)
    {
        try
        {
            variables.Delete(name);
            log.Information($"Removed firmware boot entry {name}, which pointed at an EFI system partition this deployment erased.");
        }
        catch (DeploymentStepException exception)
        {
            log.Warning(
                $"Firmware boot entry {name} pointed at an EFI system partition this deployment erased and could not be removed " +
                $"({exception.Message}). It is no longer in the boot order.");
        }
    }

    private List<ushort> DeadEntries(List<ushort> order, IReadOnlyCollection<Guid> erasedPartitionIds) =>
    [
        .. order.Distinct().Where(number => variables.Read(OptionName(number)) is { } option
            && erasedPartitionIds.Any(id => EfiLoadOption.PointsAt(option, id, path: null))),
    ];

    // The entry in the boot order that already starts the loader, or else the first one for an erased partition.
    private (ushort? Existing, ushort? Erased) FindEntries(
        List<ushort> order,
        EspPartition esp,
        string loaderPath,
        IReadOnlyCollection<Guid> erasedPartitionIds)
    {
        ushort? erased = null;

        foreach (ushort number in order)
        {
            if (variables.Read(OptionName(number)) is not { } option)
            {
                continue;
            }

            if (EfiLoadOption.PointsAt(option, esp.PartitionId, loaderPath))
            {
                return (number, erased);
            }

            if (erased is null && erasedPartitionIds.Any(id => EfiLoadOption.PointsAt(option, id, path: null)))
            {
                erased = number;
            }
        }

        return (null, erased);
    }

    private void Report(string name, string description, bool existed, bool reusedErased, bool moved)
    {
        if (existed)
        {
            log.Information(moved
                ? $"Firmware boot entry {name} already starts {description} on the new EFI system partition. It is now first in the boot order."
                : $"Firmware boot entry {name} already starts {description} on the new EFI system partition and is first in the boot order.");
        }
        else if (reusedErased)
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

    private ushort Create(EspPartition esp, string loaderPath, string description)
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

    // An array of UINT16. A stray last byte is ignored.
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
