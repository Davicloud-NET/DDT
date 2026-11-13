// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization;

namespace DDT.Contracts.Sequences;

// Installs updates from Microsoft Update until none is left, restarting as they ask. Security, critical and cumulative
// updates always; never a feature upgrade. Version 7.
public sealed record WindowsUpdateStep : SequenceStep
{
    public const int MaxExcludedKbs = 64;

    public const int MaxRestarts = 20;

    public const int DefaultRestarts = 8;

    public bool Drivers { get; init; }

    public bool OptionalUpdates { get; init; }

    // Set, not init: the generated JSON reader gives a missing init property its type's default, not this one.
    public bool MicrosoftProducts { get; set; } = true;

    // "KB5031234" or "5031234".
    public IReadOnlyList<string> ExcludedKbs { get; init; } = [];

    // The step's own limit, 1 to MaxRestarts. The engine reads it through RestartLimit.
    [JsonPropertyName("restartLimit")]
    public int AllowedRestarts { get; set; } = DefaultRestarts;

    [JsonIgnore]
    public override SequencePhase? RequiredPhase => SequencePhase.Windows;

    [JsonIgnore]
    public override int MinimumVersion => 7;

    // Searching again does no harm, and a cut-off installation is found again by the search.
    [JsonIgnore]
    public override bool Resumable => true;

    [JsonIgnore]
    public override int RestartLimit => AllowedRestarts;
}
