// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;

namespace DDT.Contracts.Settings;

// Message is English, and Text the same sentence as a code where the host said one DDT knows; an exception's own text
// has none.
public sealed record SettingApplyState(
    string Host,
    long Version,
    SettingApplyStatus State,
    string? Message,
    DateTimeOffset? UpdatedUtc,
    ServerMessage? Text = null);
