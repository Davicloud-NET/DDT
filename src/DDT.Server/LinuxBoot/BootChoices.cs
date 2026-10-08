// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Collections.Frozen;
using DDT.Pxe;
using DDT.Server.Machines;

namespace DDT.Server.LinuxBoot;

// Which boot environment each machine should start, kept in memory for ProxyDHCP, which answers every datagram at
// once and never asks the database. BootChoiceRefresher fills it. Until then, and for a machine nobody knows, the
// default of the settings applies.
public sealed class BootChoices(TimeProvider timeProvider) : IBootChoice
{
    private TaskCompletionSource? _changed;
    private Maps _maps = new(FrozenDictionary<string, BootChoiceEntry>.Empty, FrozenDictionary<string, BootChoiceEntry>.Empty);

    // The machine by the address of the adapter that asks, else by its UUID in either byte order.
    public BootWish For(NetbootMachine machine)
    {
        ArgumentNullException.ThrowIfNull(machine);

        Maps maps = Volatile.Read(ref _maps);

        if (!maps.ByMac.TryGetValue(machine.Mac, out BootChoiceEntry? entry)
            && (machine.Uuid.Length != 16 || !maps.ByUuid.TryGetValue(Convert.ToHexString(machine.Uuid.Span), out entry)))
        {
            return BootWish.None;
        }

        // A run decides. A restart the machine was asked for counts only as long as a restart may take.
        return new BootWish(
            entry.Run ?? (PendingSwitch.IsOpen(entry.NextUtc, timeProvider.GetUtcNow()) ? entry.Next : null),
            entry.LinuxRefused);
    }

    public void Replace(IEnumerable<BootChoiceEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        Dictionary<string, BootChoiceEntry> byMac = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, BootChoiceEntry> byUuid = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> shared = new(StringComparer.OrdinalIgnoreCase);

        foreach (BootChoiceEntry entry in entries)
        {
            foreach (string mac in entry.Macs)
            {
                byMac[mac] = entry;
            }

            if (entry.Uuid is { } uuid && uuid != Guid.Empty)
            {
                // SMBIOS writes the first three fields with the low byte first, RFC 4122 with the high one. Firmware
                // sends either, so both name the machine. A UUID two machines share names neither.
                foreach (string key in new[] { Convert.ToHexString(uuid.ToByteArray()), Convert.ToHexString(uuid.ToByteArray(bigEndian: true)) })
                {
                    if (byUuid.TryGetValue(key, out BootChoiceEntry? other) && !ReferenceEquals(other, entry))
                    {
                        shared.Add(key);
                    }

                    byUuid[key] = entry;
                }
            }
        }

        foreach (string key in shared)
        {
            byUuid.Remove(key);
        }

        Volatile.Write(ref _maps, new Maps(byMac.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase), byUuid.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase)));
    }

    // A machine changed, so whoever fills the list reads it again at once instead of at its next turn.
    public void Changed() => Volatile.Read(ref _changed)?.TrySetResult();

    // Ends at the next change. Taken before the list is read, so a change during the read is not missed.
    public Task NextChange()
    {
        TaskCompletionSource next = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Volatile.Write(ref _changed, next);

        return next.Task;
    }

    private sealed record Maps(FrozenDictionary<string, BootChoiceEntry> ByMac, FrozenDictionary<string, BootChoiceEntry> ByUuid);
}
