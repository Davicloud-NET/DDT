// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using DDT.Agent.Deployment;
using DDT.Core.Disks;

namespace DDT.Agent.Sequences;

// A disk's cloud-init seed that holds a run handed over to the installed Linux, as a later start of the boot
// environment finds it. No record: the token is a secret, and a record would print it.
public sealed class HandedOverSeed(LocalDisk disk, GptPartition partition)
{
    public LocalDisk Disk { get; } = disk;

    // The seed's partition in the disk's table.
    public GptPartition Partition { get; } = partition;

    // The run's token, which the agent's script wipes off the seed once the installed Linux has the run. Null then.
    public string? Token { get; init; }

    // The run the seed's state names. Null when the state is missing or cannot be read.
    public Guid? RunId { get; init; }

    // The seed's file system could not be read, so whether it holds a run at all is not known.
    public bool IsUnread { get; init; }

    // The disk's EFI system partition, which the firmware starts the installed Linux from. Null when it has none.
    public EspPartition? SystemPartition { get; init; }

    // Whether the installed Linux took the run over.
    public bool IsTaken => Token is null;

    // When the run was assigned, which a version 7 UUID begins with. Zero for a run that is not known.
    public long RunTime
    {
        get
        {
            Span<byte> bytes = stackalloc byte[16];

            return RunId is { Version: 7 } id && id.TryWriteBytes(bytes, bigEndian: true, out _)
                ? (long)(BinaryPrimitives.ReadUInt64BigEndian(bytes) >> 16)
                : 0;
        }
    }
}
