// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Settings;

// Version is the one the page loaded, so a save over someone else's is refused. Values replaces every field that is not
// locked. A secret left out of Secrets is kept. Confirm lists the warning codes the administrator accepted.
public sealed record SettingsSectionUpdate<TValues>(
    long Version,
    TValues Values,
    IReadOnlyDictionary<string, SecretUpdate>? Secrets,
    IReadOnlyList<string>? Confirm);

// Value only with Set.
public sealed record SecretUpdate(SecretAction Action, string? Value);

public enum SecretAction
{
    Keep,
    Set,
    Clear,
}
