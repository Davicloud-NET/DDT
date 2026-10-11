// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Core.CloudInit;

namespace DDT.Agent.Sequences;

// The cloud-init seed a run wrote: its files as they went onto the disk, and what its volume was built with. The
// hand-over to the installed Linux finds the volume by these and builds it again, with the agent beside its files.
public sealed record WrittenSeed(CloudInitSeedFiles Files, int PartitionNumber, long FirstSector, uint SerialNumber, DateTime Timestamp)
{
    // The volume as a hand-over found it on the disk before it put the run on it. Taking the run off puts this back.
    public SeedVolume? Found { get; init; }

    // Whether a hand-over put the run on it, token included.
    public bool HoldsRun => Found is not null;
}
