// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization;

namespace DDT.Contracts.Sequences;

// Writes a raw disk image from the library, such as a Linux distribution's cloud image, over the whole disk.
public sealed record WriteRawImageStep : SequenceStep
{
    public required Guid ImageId { get; init; }

    [JsonIgnore]
    public override SequencePhase? RequiredPhase => SequencePhase.WindowsPE;

    [JsonIgnore]
    public override bool ErasesDisk => true;

    [JsonIgnore]
    public override int MinimumVersion => 2;
}
