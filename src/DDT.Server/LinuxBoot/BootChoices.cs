// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Collections.Concurrent;
using System.Collections.Frozen;
using DDT.Contracts.Agents;
using DDT.Contracts.Images;
using DDT.Pxe;
using DDT.Server.Machines;

namespace DDT.Server.LinuxBoot;

// Which boot environment each machine should start, kept in memory for ProxyDHCP, which answers every datagram at
// once and never asks the database. BootChoiceRefresher fills it. Until then, and for a machine nobody knows, the
// default of the settings applies.
public sealed class BootChoices(TimeProvider timeProvider) : IBootChoice
{
    // What firmware reports where the board's maker set no UUID. Machines nobody knows share these values too.
    private static readonly FrozenSet<Guid> s_placeholders =
    [
        Guid.Empty,
        Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
        Guid.Parse("03000200-0400-0500-0006-000700080009"),
        Guid.Parse("12345678-1234-5678-90ab-cddeefaabbcc"),
    ];

    private readonly ConcurrentDictionary<Guid, Read> _read = new();
    private TaskCompletionSource? _changed;
    private Maps _maps = new(FrozenDictionary<string, BootChoiceEntry[]>.Empty, FrozenDictionary<string, BootChoiceEntry[]>.Empty);

    // An adapter can be listed by several machines, and so can a UUID, so one row has to be found among them. Where
    // that fails, the machine gets the default, as one nobody knows.
    public BootWish For(NetbootMachine machine)
    {
        ArgumentNullException.ThrowIfNull(machine);

        DateTimeOffset now = timeProvider.GetUtcNow();
        BootChoiceEntry[] rows = Rows(Volatile.Read(ref _maps), machine);

        // Anyone can register a row that lists another machine's adapter. It never counts against an approved one.
        if (rows.Length > 1 && rows.Any(row => row.Approved))
        {
            rows = [.. rows.Where(row => row.Approved)];
        }

        // A machine with a run or a restart it was asked for is the one that starts from the network now.
        if (rows.Length > 1 && rows.Count(row => Wanted(row, now) is not null) == 1)
        {
            rows = [rows.Single(row => Wanted(row, now) is not null)];
        }

        // The firmware of a row nobody ever approved is only what a registration claimed.
        return rows is [BootChoiceEntry found] ? new BootWish(Wanted(found, now), found.LinuxRefused && found.Approved) : BootWish.None;
    }

    public void Replace(IEnumerable<BootChoiceEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        Dictionary<string, List<BootChoiceEntry>> byMac = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, List<BootChoiceEntry>> byUuid = new(StringComparer.OrdinalIgnoreCase);

        foreach (BootChoiceEntry entry in entries)
        {
            foreach (string mac in entry.Macs.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                Add(byMac, mac, entry);
            }

            // SMBIOS writes the first three fields with the low byte first, RFC 4122 with the high one. Firmware
            // sends either, so both name the machine.
            if (entry.Uuid is { } uuid)
            {
                foreach (string key in new[] { Convert.ToHexString(uuid.ToByteArray()), Convert.ToHexString(uuid.ToByteArray(bigEndian: true)) }.Distinct())
                {
                    Add(byUuid, key, entry);
                }
            }
        }

        Volatile.Write(ref _maps, new Maps(Frozen(byMac), Frozen(byUuid)));
    }

    // Whoever fills the list reads it again at once instead of at its next turn.
    public void Changed() => Volatile.Read(ref _changed)?.TrySetResult();

    // A machine was saved. Machines poll and report every few seconds, and none of that moves what they start, so only
    // a change to what netboot reads of the machine makes the list be read again.
    public void Changed(Machine machine)
    {
        ArgumentNullException.ThrowIfNull(machine);

        Read read = new(machine.SmbiosUuid, machine.MacAddresses, machine.ActiveDeploymentId, machine.NextEnvironment, machine.NextEnvironmentUtc)
        {
            SecureBootEnabled = machine.SecureBootEnabled,
            TrustedUefiCas = machine.TrustedUefiCas,
            Approved = machine.FirstApprovedUtc is not null,
        };

        if (!_read.TryGetValue(machine.Id, out Read? before) || before != read)
        {
            _read[machine.Id] = read;
            Changed();
        }
    }

    public void Removed(IEnumerable<Guid> machineIds)
    {
        ArgumentNullException.ThrowIfNull(machineIds);

        foreach (Guid machineId in machineIds)
        {
            _read.TryRemove(machineId, out _);
        }

        Changed();
    }

    // Ends at the next change. Taken before the list is read, so a change during the read is not missed.
    public Task NextChange()
    {
        TaskCompletionSource next = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Volatile.Write(ref _changed, next);

        return next.Task;
    }

    // The rows the request could come from: the one that has both its adapter and its UUID, or else every row that
    // lists the adapter, and the row of the UUID where it names a single machine.
    private static BootChoiceEntry[] Rows(Maps maps, NetbootMachine machine)
    {
        BootChoiceEntry[] byMac = maps.ByMac.GetValueOrDefault(machine.Mac, []);
        BootChoiceEntry[] byUuid = machine.Uuid.Length == 16 ? maps.ByUuid.GetValueOrDefault(Convert.ToHexString(machine.Uuid.Span), []) : [];
        BootChoiceEntry[] both = [.. byMac.Where(row => byUuid.Contains(row, ReferenceEqualityComparer.Instance))];

        if (both.Length > 0)
        {
            return both;
        }

        // A UUID that several rows share, or that firmware fills in, tells no machine from one nobody knows.
        return byUuid.Length == 1 && !IsPlaceholder(machine.Uuid.Span) ? [.. byMac, .. byUuid] : byMac;
    }

    private static bool IsPlaceholder(ReadOnlySpan<byte> uuid) => s_placeholders.Contains(new Guid(uuid)) || s_placeholders.Contains(new Guid(uuid, bigEndian: true));

    // A run decides. A restart the machine was asked for counts only as long as a restart may take.
    private static AgentEnvironment? Wanted(BootChoiceEntry row, DateTimeOffset now) =>
        row.Run ?? (PendingSwitch.IsOpen(row.NextUtc, now) ? row.Next : null);

    private static void Add(Dictionary<string, List<BootChoiceEntry>> map, string key, BootChoiceEntry entry)
    {
        if (!map.TryGetValue(key, out List<BootChoiceEntry>? rows))
        {
            map[key] = rows = [];
        }

        rows.Add(entry);
    }

    private static FrozenDictionary<string, BootChoiceEntry[]> Frozen(Dictionary<string, List<BootChoiceEntry>> map) =>
        map.ToFrozenDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.OrdinalIgnoreCase);

    private sealed record Maps(FrozenDictionary<string, BootChoiceEntry[]> ByMac, FrozenDictionary<string, BootChoiceEntry[]> ByUuid);

    // What netboot reads of a machine's row, as it was when the machine last changed in this process.
    private sealed record Read(string Uuid, string Macs, Guid? Run, AgentEnvironment? Next, DateTimeOffset? NextUtc)
    {
        public bool? SecureBootEnabled { get; init; }

        public UefiCa? TrustedUefiCas { get; init; }

        public bool Approved { get; init; }
    }
}
