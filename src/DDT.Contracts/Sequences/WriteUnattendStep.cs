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

    // Null is the image's language.
    public string? UiLanguage { get; init; }

    // Null is the Deployment defaults' value.
    public string? Organization { get; init; }

    public string? Owner { get; init; }

    // The out-of-box pages the answer file hides. All on is the answer file before these switches.
    public bool HideEulaPage { get; init; } = true;

    public bool HideOemRegistration { get; init; } = true;

    public bool HideOnlineAccounts { get; init; } = true;

    public bool HideWirelessSetup { get; init; } = true;

    [JsonIgnore]
    public override SequencePhase? RequiredPhase => SequencePhase.WindowsPE;
}
