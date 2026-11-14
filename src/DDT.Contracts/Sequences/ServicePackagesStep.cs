// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization;

namespace DDT.Contracts.Sequences;

// Adds Windows packages, language packs, features on demand and updates, to the applied Windows offline with DISM.
// MatchingUpdates adds every update package marked for the image's build. Version 8.
public sealed record ServicePackagesStep : SequenceStep
{
    public const int MaxPackages = 64;

    public IReadOnlyList<Guid> Packages { get; init; } = [];

    public bool MatchingUpdates { get; init; }

    // Set, not init: the generated JSON reader gives a missing init property its type's default, not this one.
    public bool Cleanup { get; set; } = true;

    [JsonIgnore]
    public override SequencePhase? RequiredPhase => SequencePhase.WindowsPE;

    [JsonIgnore]
    public override int MinimumVersion => 8;
}
