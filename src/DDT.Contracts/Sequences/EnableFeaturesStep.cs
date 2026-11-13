// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization;

namespace DDT.Contracts.Sequences;

// Turns on Windows roles and features with DISM, by the names DISM lists, such as NetFx3. Version 7.
public sealed record EnableFeaturesStep : SequenceStep
{
    public const int MaxFeatures = 64;

    public const int MaxNameLength = 128;

    public IReadOnlyList<string> Features { get; init; } = [];

    [JsonIgnore]
    public override SequencePhase? RequiredPhase => SequencePhase.Windows;

    [JsonIgnore]
    public override int MinimumVersion => 7;

    // Enabling a feature again does no harm.
    [JsonIgnore]
    public override bool Resumable => true;

    [JsonIgnore]
    public override int RestartLimit => 3;
}
