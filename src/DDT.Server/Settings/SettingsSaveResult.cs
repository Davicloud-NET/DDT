// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Settings;

namespace DDT.Server.Settings;

public sealed record SettingsSaveResult(
    SettingsSaveOutcome Outcome,
    SettingsSnapshot? Snapshot = null,
    // Problems and Unconfirmed name their fields as the page does.
    IReadOnlyList<SettingMessage>? Problems = null,
    IReadOnlyList<SettingMessage>? Unconfirmed = null,
    IReadOnlyList<string>? Fields = null);
