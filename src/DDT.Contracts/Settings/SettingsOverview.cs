// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Settings;

// KeyRingReadable: false when this server cannot read the key ring the stored secrets were encrypted with, and then it
// saves no settings at all. Server lists what configuration alone decides, read-only.
public sealed record SettingsOverview(IReadOnlyList<SettingsSectionSummary> Sections, IReadOnlyList<ServerSetting> Server, bool KeyRingReadable);

public sealed record SettingsSectionSummary(
    string Section,
    SettingsSectionKind Kind,
    long Version,
    DateTimeOffset? UpdatedUtc,
    string? UpdatedBy,
    int LockedCount,
    int ProblemCount,
    IReadOnlyList<SettingApplyState>? Apply);

public enum SettingsSectionKind
{
    // The next use reads the new value.
    Live,

    // The component that uses it is rebuilt inside the running server on save.
    Restart,
}

// Value is null when the setting is not shown, because it is secret by rule or not on the list of values that are.
// Source names where it comes from, such as an environment variable or appsettings.json, and is null when unset.
public sealed record ServerSetting(string Key, string? Value, bool IsSet, string? Source, bool Secret);
