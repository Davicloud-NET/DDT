// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Nodes;
using DDT.Contracts.Settings;

namespace DDT.Server.Settings;

// A save of one section. Values is the whole section as its option class serializes it; a field configuration locks is
// taken from it but not written. A secret missing from Secrets is kept. DirectoryProof is the token an LDAP test gives a
// directory administrator for exactly these values.
public sealed class SettingsUpdate
{
    public required long Version { get; init; }

    public required JsonObject Values { get; init; }

    public IReadOnlyDictionary<string, SecretUpdate> Secrets { get; init; } = new Dictionary<string, SecretUpdate>();

    public IReadOnlySet<string> Confirmed { get; init; } = new HashSet<string>();

    public bool Reauthenticated { get; init; }

    public string? DirectoryProof { get; init; }
}

public enum SettingsSaveOutcome
{
    Saved,

    // Someone saved the section since the version the update names.
    Conflict,

    // Problems, a refused secret or an unconfirmed warning: nothing was saved.
    Invalid,

    // The update changes fields that need a fresh proof of identity, which Fields names.
    Reauthenticate,

    // This process cannot read the key ring of the stored secrets, so it saves nothing.
    KeyRingUnreadable,
}

// Problems and Unconfirmed name their fields as the page does.
public sealed record SettingsSaveResult(
    SettingsSaveOutcome Outcome,
    SettingsSnapshot? Snapshot = null,
    IReadOnlyList<SettingMessage>? Problems = null,
    IReadOnlyList<SettingMessage>? Unconfirmed = null,
    IReadOnlyList<string>? Fields = null);
