// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization;

namespace DDT.Contracts.Sequences;

// Sets a variable that the sequence declares with SetBySteps. Value is a template, such as
// PC-{{SerialNumber|right:12}}, that the agent fills in when the step runs.
public sealed record SetVariableStep : SequenceStep
{
    public required string Variable { get; init; }

    public string Value { get; init; } = "";

    [JsonIgnore]
    public override SequencePhase? RequiredPhase => null;

    [JsonIgnore]
    public override int MinimumVersion => 3;

    [JsonIgnore]
    public override bool Resumable => true;
}
