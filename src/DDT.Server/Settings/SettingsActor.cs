// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Settings;

// Who a change is recorded for: a signed-in administrator, the console verbs, or configuration at startup.
public sealed record SettingsActor(Guid? UserId, string? Name, string? Address)
{
    public static SettingsActor Console { get; } = new(null, "console", null);

    public static SettingsActor Configuration { get; } = new(null, "configuration", null);
}
