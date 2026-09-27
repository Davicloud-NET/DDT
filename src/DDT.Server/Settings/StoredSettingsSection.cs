// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Nodes;

namespace DDT.Server.Settings;

// One row of ddt."SettingsSections" as read, its secrets decrypted. Values holds only the fields ever written, so an
// absent field was never written and takes its configured value or its default.
public sealed record StoredSettingsSection(
    string Section,
    int SchemaVersion,
    JsonObject Values,
    IReadOnlyDictionary<string, StoredSecret> Secrets,
    long Version,
    DateTimeOffset? UpdatedUtc,
    Guid? UpdatedByUserId,
    string? UpdatedByName)
{
    public static StoredSettingsSection Empty(string section) =>
        new(section, SettingsStore.SchemaVersion, [], new Dictionary<string, StoredSecret>(), 0, null, null, null);
}

// Value is null for a secret that was cleared, or that no longer decrypts, which Unreadable then says. Protected is the
// ciphertext as stored, kept so that a secret this process cannot read survives a save of the other fields.
public sealed record StoredSecret(string? Value, bool Unreadable, DateTimeOffset UpdatedUtc, string? Protected)
{
    // The generated ToString would print the secret into any log or assertion message that shows one.
    public override string ToString() => nameof(StoredSecret);
}
