// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization;

namespace DDT.Contracts.Sequences;

// Null settings use the server's DDT:Deployment defaults. LocalAdministrator adds the account configured there, with
// its password. The password stays in the server's configuration.
public sealed record WriteUnattendStep : SequenceStep
{
    public const int MaxFirstLogonCommands = 20;

    public const int MaxCommandLength = 1024;

    public const int MaxAutoLogonCount = 99;

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
    // Set, not init: the generated JSON reader gives a missing init property its type's default, not this one.
    public bool HideEulaPage { get; set; } = true;

    public bool HideOemRegistration { get; set; } = true;

    public bool HideOnlineAccounts { get; set; } = true;

    public bool HideWirelessSetup { get; set; } = true;

    // Version 7. Run in order at the first sign-in of an administrator. In the answer file when the sequence has no
    // Windows step, otherwise set up by the agent as the run ends.
    public IReadOnlyList<string> FirstLogonCommands { get; init; } = [];

    public AutoLogonKind AutoLogon { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AccountReference? AutoLogonAccount { get; init; }

    public int AutoLogonCount { get; set; } = 1;

    [JsonIgnore]
    public bool SetsFirstLogon => (FirstLogonCommands ?? []).Count > 0 || AutoLogon != AutoLogonKind.None;

    [JsonIgnore]
    public override SequencePhase? RequiredPhase => SequencePhase.WindowsPE;

    // An older agent would not set them up as a run in Windows ends.
    [JsonIgnore]
    public override int MinimumVersion => SetsFirstLogon ? 7 : 1;
}
