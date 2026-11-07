// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;
using DDT.Contracts.Sequences;

namespace DDT.Core.Sequences;

// Where a step may come among the phases of its sequence's kind. StepRules leaves out a step of the other kind.
internal static class PhaseOrder
{
    public static void Check(StepPlace place, SequenceKind kind, Action<ServerMessage> add)
    {
        PathPhases before = place.Before.Phases;
        bool afterInstalled = place.Required is { } asked
            ? SequenceKinds.IsBoot(asked) && (before & (PathPhases.Installed | PathPhases.BootAfterInstalled)) != 0
            : (before & PathPhases.BootAfterInstalled) != 0;

        if (afterInstalled)
        {
            add((kind == SequenceKind.Linux ? ServerMessages.SequenceLinuxBootAfterLinux : ServerMessages.SequenceWindowsPEAfterWindows).With());
        }

        if (place.InRepeat && place.Required is { } phase && place.Before.Current != Role(phase))
        {
            add(ServerMessages.SequencePhaseChangeInRepeat.With("kind", kind.ToString()));
        }

        if (Missing(place) is { } missing)
        {
            add(missing.With());
        }
    }

    public static PhaseSet Role(SequencePhase phase) => SequenceKinds.IsBoot(phase) ? PhaseSet.Boot : PhaseSet.Installed;

    // What a step in the installed system lacks: Windows its image, Linux the seed that carries the agent there.
    private static MessageTemplate? Missing(StepPlace place)
    {
        PathState before = place.Before;

        if (place.Required == SequencePhase.Windows && !before.MustHave(Happened.ImageEveryTime))
        {
            // When only some paths apply an image, the paths are the problem, not the step's conditions.
            return !before.MustHave(Happened.ImageApplied) && before.MayHave(Happened.ImageApplied)
                ? ServerMessages.SequenceWindowsNeedsImageOnEveryPath
                : ServerMessages.SequenceWindowsNeedsImage;
        }

        if (place.Required == SequencePhase.Linux && !before.MustHave(Happened.SeedEveryTime))
        {
            // A seed step that some path passes is fine itself, so there the paths are the problem.
            return before.MayHave(Happened.SeedEveryTime)
                ? ServerMessages.SequenceLinuxNeedsSeedOnEveryPath
                : ServerMessages.SequenceLinuxNeedsSeed;
        }

        return null;
    }
}
