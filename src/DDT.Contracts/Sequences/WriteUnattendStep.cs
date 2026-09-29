// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization;

namespace DDT.Contracts.Sequences;

// Null settings use the server's DDT:Deployment defaults. LocalAdministrator adds the account configured there, with
// its password. The password stays in the server's configuration.
public sealed record WriteUnattendStep : SequenceStep
{
    public string? TimeZone { get; init; }

    public string? Locale { get; init; }

    public string? Keyboard { get; init; }

    public bool LocalAdministrator { get; init; }

    [JsonIgnore]
    public override SequencePhase? RequiredPhase => SequencePhase.WindowsPE;
}
