// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization;

namespace DDT.Contracts.Sequences;

// Waits until someone continues the run at the machine or on the web, or ContinueAfterMinutes (1 to 1440) have passed.
// Null waits for as long as it takes. Message is a template, shown at the machine and on the machine's page.
public sealed record PauseStep : SequenceStep
{
    public string Message { get; init; } = "";

    public int? ContinueAfterMinutes { get; init; }

    [JsonIgnore]
    public override SequencePhase? RequiredPhase => null;

    [JsonIgnore]
    public override int MinimumVersion => 3;

    [JsonIgnore]
    public override bool Resumable => true;
}
