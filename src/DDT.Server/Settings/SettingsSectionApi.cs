// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Settings;

namespace DDT.Server.Settings;

// How a section appears to the page. Mapping field by field onto records in DDT.Contracts keeps secrets and
// configuration-only values off the page, and lets the stored document change without changing the API.
public abstract class SettingsSectionApi
{
    public abstract SettingsSectionDefinition Definition { get; }

    public string Name => Definition.Name;

    // Operators may read these, and nothing else of the page: what an assignment will do depends on them.
    public bool OperatorsMayRead => Name is SettingsSectionNames.Deployment or SettingsSectionNames.Machines;

    public abstract object View(
        SettingsSectionState state,
        IReadOnlyList<SettingMessage> warnings,
        IReadOnlyList<SettingApplyState>? apply);
}
