// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization;

namespace DDT.Contracts.Sequences;

// Adds a partition labelled CIDATA at the end of the disk a raw image was written to, holding cloud-init's NoCloud
// seed. The texts may use placeholders for the machine's values, such as {{ComputerName}}.
public sealed record WriteCloudInitSeedStep : SequenceStep
{
    public string MetaData { get; init; } = "";

    // Every signed-in user can read it, so passwords go in hashed.
    public string UserData { get; init; } = "";

    public string? NetworkConfig { get; init; }

    [JsonIgnore]
    public override SequencePhase? RequiredPhase => SequencePhase.WindowsPE;

    [JsonIgnore]
    public override int MinimumVersion => 2;
}
