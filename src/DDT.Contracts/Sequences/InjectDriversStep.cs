// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization;

namespace DDT.Contracts.Sequences;

// The server picks the driver packages by the machine's model. RequireMatch fails the step when it has none.
public sealed record InjectDriversStep : SequenceStep
{
    public bool RequireMatch { get; init; }

    [JsonIgnore]
    public override SequencePhase? RequiredPhase => SequencePhase.WindowsPE;
}
