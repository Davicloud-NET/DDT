// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization;

namespace DDT.Contracts.Sequences;

// Adds a partition labelled CIDATA at the end of the disk a raw image was written to, holding cloud-init's NoCloud
// seed: meta-data, user-data and, unless null, network-config. The texts may use placeholders for the machine's
// values, such as {{ComputerName}}. Every signed-in user can read them, so passwords go in hashed.
public sealed record WriteCloudInitSeedStep : SequenceStep
{
    public string MetaData { get; init; } = "";

    public string UserData { get; init; } = "";

    public string? NetworkConfig { get; init; }

    [JsonIgnore]
    public override SequencePhase? RequiredPhase => SequencePhase.WindowsPE;

    [JsonIgnore]
    public override int MinimumVersion => 2;
}
