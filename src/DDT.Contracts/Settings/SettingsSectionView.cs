// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;

namespace DDT.Contracts.Settings;

// One section of the settings page as it applies now. Values holds what applies, a configured value where one is set;
// Secrets holds only whether each secret is set, never its value. Locked names the fields configuration sets, which a
// save leaves alone. Problems keep the section closed until they are fixed; warnings do not. Apply is null for a
// section that applies live, and otherwise says for every host whether it applied this version. Reauthenticate names
// the fields a save may change only with a token from POST /api/settings/reauthenticate.
public sealed record SettingsSectionView<TValues>(
    string Section,
    long Version,
    DateTimeOffset? UpdatedUtc,
    string? UpdatedBy,
    TValues Values,
    IReadOnlyDictionary<string, SecretState> Secrets,
    IReadOnlyList<SettingLock> Locked,
    IReadOnlyList<SettingMessage> Problems,
    IReadOnlyList<SettingMessage> Warnings,
    IReadOnlyList<SettingApplyState>? Apply,
    IReadOnlyList<string> Reauthenticate);

// Unreadable: a secret was stored, but this server's key ring cannot decrypt it, so it has to be entered again.
public sealed record SecretState(bool IsSet, bool Unreadable, DateTimeOffset? UpdatedUtc);

// ConfigurationKey and EnvironmentVariable are the two spellings of the key that sets the field, such as
// DDT:Deployment:Domain:Name and DDT__Deployment__Domain__Name. StoredDiffers: the page's own value, which applies
// again once the key is removed, is not the configured one.
public sealed record SettingLock(string Field, string ConfigurationKey, string EnvironmentVariable, string Source, bool StoredDiffers);

// Field is the field's name on the page, such as domain.name or bootTargets[X64Uefi].method, and empty for the whole
// section. Message is English, and Text the same sentence as a code with its values, for a client that says it in the
// person's language. Code names a warning a save has to confirm, such as network.wide, and is null for a problem.
public sealed record SettingMessage(string Field, string Message, string? Code, ServerMessage? Text = null);

// Message is English, and Text the same sentence as a code where the host said one DDT knows; an exception's own text
// has none.
public sealed record SettingApplyState(
    string Host,
    long Version,
    SettingApplyStatus State,
    string? Message,
    DateTimeOffset? UpdatedUtc,
    ServerMessage? Text = null);

public enum SettingApplyStatus
{
    Applied,
    Failed,

    // The host has not applied this version yet.
    Pending,
}
