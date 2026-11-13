// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;
using DDT.Core.Sequences;

namespace DDT.Agent.WindowsUpdates;

// Picks what a Windows Update step installs from what the search found.
public static class UpdateFilter
{
    public const string FeatureUpgrade = "a feature upgrade";

    public const string Driver = "a driver";

    public const string Optional = "optional";

    public const string WantsInput = "it may ask for input";

    public const string Excluded = "the step leaves it out";

    public const string GaveUp = "it failed twice";

    public static UpdateSelection Choose(IReadOnlyList<AvailableUpdate> found, WindowsUpdateStep step, UpdateCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(found);
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(checkpoint);

        HashSet<string> excluded = [.. (step.ExcludedKbs ?? []).Select(KbNumbers.Normalize).OfType<string>()];
        List<AvailableUpdate> install = [];
        List<LeftOutUpdate> leftOut = [];

        foreach (AvailableUpdate update in found)
        {
            if (Reason(update, step, excluded, checkpoint) is { } reason)
            {
                leftOut.Add(new LeftOutUpdate(update, reason));
            }
            else
            {
                install.Add(update);
            }
        }

        return new UpdateSelection(install, leftOut);
    }

    private static string? Reason(AvailableUpdate update, WindowsUpdateStep step, HashSet<string> excluded, UpdateCheckpoint checkpoint)
    {
        if (UpdateKinds.IsFeatureUpgrade(update))
        {
            return FeatureUpgrade;
        }

        if (UpdateKinds.IsDriver(update) && !step.Drivers)
        {
            return Driver;
        }

        if (UpdateKinds.IsOptional(update) && !step.OptionalUpdates)
        {
            return Optional;
        }

        if (UpdateKinds.WantsInput(update))
        {
            return WantsInput;
        }

        if (update.Kbs.Any(excluded.Contains))
        {
            return Excluded;
        }

        return checkpoint.GaveUp(update.Id) ? GaveUp : null;
    }
}
