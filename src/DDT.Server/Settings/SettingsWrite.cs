// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Data;

namespace DDT.Server.Settings;

// A section written as its new version. Row is the one read, null for a section never written; the audit rows say Lead,
// then Changes. Publish applies the section in this process at once.
internal sealed record SettingsWrite(
    SettingsSectionDefinition Definition,
    SettingsSection? Row,
    StoredSettingsSection Section,
    Actor Actor,
    string Action,
    string Lead,
    IReadOnlyList<string> Changes,
    DateTimeOffset Now)
{
    public bool Publish { get; init; } = true;
}
