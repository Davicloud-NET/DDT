// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization;

namespace DDT.Contracts.Sequences;

// Starts encrypting the empty Windows partition in Windows PE with a clear key, so the image is written encrypted.
// Enable BitLocker adds the protectors later. Version 8.
public sealed record PreprovisionBitLockerStep : SequenceStep
{
    public BitLockerUnavailable IfUnavailable { get; init; }

    [JsonIgnore]
    public override SequencePhase? RequiredPhase => SequencePhase.WindowsPE;

    [JsonIgnore]
    public override int MinimumVersion => 8;
}
