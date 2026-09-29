// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Settings;

public sealed record SettingsSectionUpdate<TValues>(
    // The version the page loaded, so a save over someone else's changes is refused.
    long Version,
    // Replaces every field that is not locked.
    TValues Values,
    // A secret left out is kept.
    IReadOnlyDictionary<string, SecretUpdate>? Secrets,
    // The warning codes the administrator accepted.
    IReadOnlyList<string>? Confirm);
