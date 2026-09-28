// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;

namespace DDT.Server.Deployments;

// The steps of a run's path as far as it is decided, which a list of runs counts and shows as a rail: the leaves of its
// tree in pre-order, but none in the branch an IF did not take, and of an IF that has not decided yet those of the branch
// with more steps, Then when both have as many. In a container that was skipped, or that failed and caught the failure,
// only the steps that ran are on the path. A step a repeat runs again counts once. A flat run's path is all its steps.
public static class RunPaths
{
    // Tree is the run's definition, or null for a run without containers, whose rows say all there is.
    public static IReadOnlyList<Guid> Leaves(SequenceDefinition? tree, IReadOnlyList<DeploymentStep> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        if (tree is null)
        {
            return [.. rows.Where(row => !RunSnapshots.IsContainer(row)).OrderBy(row => row.Index).Select(row => row.StepId)];
        }

        Dictionary<Guid, DeploymentStep> byId = [];

        foreach (DeploymentStep row in rows)
        {
            byId.TryAdd(row.StepId, row);
        }

        return Series(tree.Steps, byId, open: true);
    }

    // The node's place on the path, counting from 0, as DeploymentSummary.StepIndex has it: a step's own, or for a node
    // off the path, such as a container, the number of steps on the path before it.
    public static int Number(IReadOnlyList<Guid> path, IReadOnlyList<DeploymentStep> rows, DeploymentStep node)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(node);

        for (int own = 0; own < path.Count; own++)
        {
            if (path[own] == node.StepId)
            {
                return own;
            }
        }

        HashSet<Guid> before = [.. rows.Where(row => row.Index < node.Index).Select(row => row.StepId)];

        return path.Count(before.Contains);
    }

    private static List<Guid> Series(IReadOnlyList<SequenceStep?>? steps, Dictionary<Guid, DeploymentStep> rows, bool open) =>
        [.. (steps ?? []).OfType<SequenceStep>().SelectMany(step => Node(step, rows, open))];

    // Open is whether the steps that have not run yet are still to come.
    private static List<Guid> Node(SequenceStep step, Dictionary<Guid, DeploymentStep> rows, bool open)
    {
        DeploymentStep? row = rows.GetValueOrDefault(step.Id);

        if (!step.IsContainer)
        {
            return open || row is { State: not StepState.Pending } ? [step.Id] : [];
        }

        bool inside = open && row is not { State: StepState.Skipped or StepState.Failed };

        if (step is IfStep choice)
        {
            List<Guid> then = Series(choice.Then, rows, inside);
            List<Guid> otherwise = Series(choice.Else, rows, inside);

            return row?.Branch switch
            {
                IfBranch.Then => then,
                IfBranch.Else => otherwise,
                _ => then.Count >= otherwise.Count ? then : otherwise,
            };
        }

        return [.. step.Bodies.SelectMany(body => Series(body.Steps, rows, inside))];
    }
}
