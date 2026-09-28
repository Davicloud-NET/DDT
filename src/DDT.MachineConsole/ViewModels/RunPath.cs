// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using DDT.ConsoleProtocol;
using DDT.MachineConsole.Texts;

namespace DDT.MachineConsole.ViewModels;

// The steps the rail shows: a tree's leaves in pre-order, without group, IF and repeat nodes. A nested leaf skipped with
// Pass 0 lies off the path, as on the branch an IF did not take; before an IF decides, both branches count.
public static class RunPath
{
    public static IReadOnlyList<ConsoleStep> Steps(ConsoleRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        HashSet<Guid> containers = [.. run.Steps.Select(step => step.ParentId).OfType<Guid>()];

        return [.. run.Steps.Where(step => !IsContainer(step, containers) && !IsOffThePath(step))];
    }

    // Any node inside another: the run is a tree, whose path the screens name as such.
    public static bool IsTree(ConsoleRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        return run.Steps.Any(step => step.ParentId is not null || IsContainerKind(step.Kind));
    }

    // The step's place on the path, from 0, or null where it is not on it.
    public static int? IndexOf(IReadOnlyList<ConsoleStep> steps, Guid? id)
    {
        ArgumentNullException.ThrowIfNull(steps);

        for (int index = 0; id is not null && index < steps.Count; index++)
        {
            if (steps[index].Id == id)
            {
                return index;
            }
        }

        return null;
    }

    // One module per step, numbered along the path. The step the run waits at, paused or for answers, is marked as
    // awaiting someone rather than as running.
    public static IReadOnlyList<RailStep> Rail(Localizer l, ConsoleRun run, IReadOnlyList<ConsoleStep> steps)
    {
        ArgumentNullException.ThrowIfNull(l);
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(steps);

        return [.. steps.Select((step, index) =>
        {
            bool awaits = step.Id == run.CurrentStepId
                && step.State == ConsoleStepState.Running
                && run.Activity is ConsoleActivity.Paused or ConsoleActivity.WaitingForInput;
            string state = awaits
                ? run.Activity == ConsoleActivity.Paused ? l.T("Paused") : l.T("Waiting")
                : Say.StepState(l, step.State);

            return new RailStep(
                (index + 1).ToString("00", CultureInfo.InvariantCulture),
                step.Name,
                step.State,
                step.Id == run.CurrentStepId && !awaits ? run.Percent : null,
                l.F("Step {number}, {name}: {state}", ("number", l.Number(index + 1)), ("name", step.Name), ("state", state)),
                awaits);
        })];
    }

    // The phases the steps run in, above the rail, where they run in more than one.
    public static IReadOnlyList<RailPhase> Phases(Localizer l, IReadOnlyList<ConsoleStep> steps)
    {
        ArgumentNullException.ThrowIfNull(l);
        ArgumentNullException.ThrowIfNull(steps);

        if (steps.Select(step => step.Phase).Distinct().Count() < 2)
        {
            return [];
        }

        List<RailPhase> phases = [];

        foreach (ConsoleStep step in steps)
        {
            if (phases.Count > 0 && phases[^1].Phase == step.Phase)
            {
                phases[^1] = phases[^1] with { Steps = phases[^1].Steps + 1 };
            }
            else
            {
                phases.Add(new RailPhase(step.Phase, Say.Phase(l, step.Phase), 1));
            }
        }

        return phases;
    }

    private static bool IsContainer(ConsoleStep step, HashSet<Guid> containers) => IsContainerKind(step.Kind) || containers.Contains(step.Id);

    private static bool IsContainerKind(string kind) => kind is "group" or "if" or "repeat";

    // A leaf at the top is on every path, and a flat run has only those.
    private static bool IsOffThePath(ConsoleStep step) => step.ParentId is not null && step.State == ConsoleStepState.Skipped && step.Pass == 0;
}
