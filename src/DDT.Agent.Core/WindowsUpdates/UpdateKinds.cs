// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.WindowsUpdates;

// What an update is, by category id and type. Names are localised, so they are never read.
public static class UpdateKinds
{
    public static readonly Guid Upgrades = Guid.Parse("3689bdc8-b205-4af4-8d4a-a63924c5e9d5");

    public static readonly Guid Drivers = Guid.Parse("ebfc1fc5-71a4-4f7b-9aca-3b9a503104a0");

    public static bool IsFeatureUpgrade(AvailableUpdate update) => update.CategoryIds.Contains(Upgrades);

    public static bool IsDriver(AvailableUpdate update) => update.Type == UpdateType.Driver || update.CategoryIds.Contains(Drivers);

    public static bool IsOptional(AvailableUpdate update) => update.BrowseOnly || update.IsBeta;

    // Every driver says it may want input, so only a software update is taken at its word.
    public static bool WantsInput(AvailableUpdate update) => update.CanRequestUserInput && !IsDriver(update);
}
