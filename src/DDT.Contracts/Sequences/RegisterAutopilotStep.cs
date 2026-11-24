// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization;

namespace DDT.Contracts.Sequences;

// Collects the machine's Autopilot hardware hash and serial number and hands them to the server, which uploads them.
// No group tag takes the Autopilot settings' default. Version 8.
public sealed record RegisterAutopilotStep : SequenceStep
{
    public const int MaxGroupTagLength = 128;

    public const int MaxWaitMinutes = 120;

    public const int DefaultWaitMinutes = 30;

    public string? GroupTag { get; init; }

    public bool WaitForImport { get; init; }

    // Set, not init: the generated JSON reader gives a missing init property its type's default, not this one.
    public int WaitMinutes { get; set; } = DefaultWaitMinutes;

    [JsonIgnore]
    public override SequencePhase? RequiredPhase => SequencePhase.Windows;

    [JsonIgnore]
    public override int MinimumVersion => 8;

    // Collecting again does no harm; the server keeps the latest collection.
    [JsonIgnore]
    public override bool Resumable => true;
}
