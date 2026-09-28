// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Nodes;
using DDT.Contracts.Settings;
using DDT.Core.Configuration;

namespace DDT.Server.Settings;

// One section in a snapshot.
public sealed class SettingsSectionState
{
    public required SettingsSectionDefinition Definition { get; init; }

    public string Name => Definition.Name;

    public required long Version { get; init; }

    public DateTimeOffset? UpdatedUtc { get; init; }

    public string? UpdatedBy { get; init; }

    // What applies, secrets left out.
    public required JsonObject Values { get; init; }

    // What the page saved, over the defaults; it applies again once configuration stops setting a field.
    public required JsonObject StoredValues { get; init; }

    public required object Options { get; init; }

    public required IReadOnlyList<SettingLockState> Locks { get; init; }

    public required IReadOnlyList<SettingProblem> Problems { get; init; }

    public required IReadOnlyList<SettingWarning> Warnings { get; init; }

    public required IReadOnlyDictionary<string, SecretState> Secrets { get; init; }

    public StoredSettingsSection? Stored { get; init; }

    public bool Closed => Problems.Count > 0;

    public bool IsLocked(SettingField field) => Locks.Any(settingLock => settingLock.Field == field);
}
