// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;

namespace DDT.Contracts.Settings;

// Message is in English. Text holds the same sentence as a code, when the host reported one that DDT knows. An
// exception's own text has no code.
public sealed record SettingApplyState(
    string Host,
    long Version,
    SettingApplyStatus State,
    string? Message,
    DateTimeOffset? UpdatedUtc,
    ServerMessage? Text = null);
