// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Nodes;

namespace DDT.Server.Settings;

// One row of ddt."SettingsSections" as read, its secrets decrypted.
public sealed record StoredSettingsSection(
    string Section,
    int SchemaVersion,
    // Only the fields ever written; an absent one takes its configured value or its default.
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
