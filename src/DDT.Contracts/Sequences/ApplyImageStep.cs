// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization;

namespace DDT.Contracts.Sequences;

public sealed record ApplyImageStep : SequenceStep
{
    public required Guid ImageId { get; init; }

    [JsonIgnore]
    public override SequencePhase? RequiredPhase => SequencePhase.WindowsPE;
}
