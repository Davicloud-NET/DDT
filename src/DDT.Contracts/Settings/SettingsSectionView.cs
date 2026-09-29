// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Settings;

// One section of the settings page as it applies now.
public sealed record SettingsSectionView<TValues>(
    string Section,
    long Version,
    DateTimeOffset? UpdatedUtc,
    string? UpdatedBy,
    // The values that apply. A configured value wins where one is set.
    TValues Values,
    // Only whether each secret is set, never its value.
    IReadOnlyDictionary<string, SecretState> Secrets,
    // Fields that configuration sets. A save leaves them alone.
    IReadOnlyList<SettingLock> Locked,
    // Problems make the section fail closed until they're fixed. Warnings don't.
    IReadOnlyList<SettingMessage> Problems,
    IReadOnlyList<SettingMessage> Warnings,
    // Null for a section that applies live. Otherwise it says whether each host applied this version.
    IReadOnlyList<SettingApplyState>? Apply,
    // Fields a save may change only with a token from POST /api/settings/reauthenticate.
    IReadOnlyList<string> Reauthenticate);
