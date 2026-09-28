// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Sequences;

// Walks a definition's steps as a tree, for the server, the agent and the tests alike. Every walk is in pre-order,
// so a node comes first, then its bodies in the order of Bodies. Documents come from outside, so null lists and null
// nodes are skipped. When an id repeats, Index keeps the first node with it, and the validator reports the repeat.
public static class SequenceTree
{
    // The most conditions a step may have in versions 1 and 2. Agents of those versions check this before a run, so a
    // When only moves into Conditions if the total stays within it.
    public const int LegacyMaxConditions = 10;

    private const int TreeVersion = 3;

    // Every node, including containers and the contents of their bodies.
    public static IReadOnlyList<SequenceStep> Nodes(SequenceDefinition definition) =>
        [.. Walk(definition).Select(position => position.Step)];

    public static IReadOnlyList<SequenceStep> Leaves(SequenceDefinition definition) =>
        [.. Walk(definition).Select(position => position.Step).Where(node => !node.IsContainer)];

    public static IReadOnlyDictionary<Guid, NodePosition> Index(SequenceDefinition definition)
    {
        Dictionary<Guid, NodePosition> index = [];

        foreach (NodePosition position in Walk(definition))
        {
            index.TryAdd(position.Step.Id, position);
        }

        return index;
    }

    // Where a run continues once the node is over, whether it ran or was skipped. That's its next sibling, or leaving
    // its parent when it was the last node of its body, or null after the last node at the top.
    public static NodeCursor? Successor(SequenceDefinition definition, IReadOnlyDictionary<Guid, NodePosition> index, Guid nodeId)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(index);

        if (!index.TryGetValue(nodeId, out NodePosition? position))
        {
            throw new ArgumentException("The node is not in the definition's tree.", nameof(nodeId));
        }

        IReadOnlyList<SequenceStep?> siblings = position.ParentId is { } parentId
            ? index[parentId].Step.Bodies.First(body => body.Name == position.Body).Steps
            : definition.Steps ?? [];

        for (int sibling = position.SiblingIndex + 1; sibling < siblings.Count; sibling++)
        {
            if (siblings[sibling] is { } next)
            {
                return new NodeCursor(next.Id, false);
            }
        }

        return position.ParentId is { } parent ? new NodeCursor(parent, true) : null;
    }

    // The lowest version whose agents can run the whole definition as written. It's 3 for any tree feature, and for a
    // condition on a name outside MachineVariableNames.All, which an older agent would treat as false. MinimumVersion
    // covers the kinds and their own members.
    public static int RequiredVersion(SequenceDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        int version = definition.Variables is { Count: > 0 } || definition.Inputs is { Count: > 0 } ? TreeVersion : 1;

        foreach (NodePosition position in Walk(definition))
        {
            SequenceStep node = position.Step;
            version = Math.Max(version, node.MinimumVersion);

            if (node.When is not null
                || node.Shares is { Count: > 0 }
                || (node.Conditions?.Any(condition => condition is not null && NeedsTree(condition.Variable, condition.Operator)) ?? false))
            {
                version = Math.Max(version, TreeVersion);
            }
        }

        return version;
    }

    // Rebuilds the tree from the leaves up. A node's bodies go through map first, then the node itself. A list in
    // which nothing changed is returned as it was, and null nodes stay where they are.
    public static IReadOnlyList<SequenceStep> Map(IReadOnlyList<SequenceStep> steps, Func<SequenceStep, SequenceStep> map)
    {
        ArgumentNullException.ThrowIfNull(steps);
        ArgumentNullException.ThrowIfNull(map);

        SequenceStep[]? changed = null;

        for (int index = 0; index < steps.Count; index++)
        {
            if (steps[index] is not { } step)
            {
                continue;
            }

            SequenceStep mapped = map(MapBodies(step, map));

            if (!ReferenceEquals(mapped, step))
            {
                changed ??= [.. steps];
                changed[index] = mapped;
            }
        }

        return changed ?? steps;
    }

    // What Normalised stores. Empty version 3 lists become null, and a When that an older agent can run moves into
    // Conditions.
    internal static SequenceDefinition Fold(SequenceDefinition definition) =>
        definition with
        {
            Steps = definition.Steps is { } steps ? Map(steps, FoldStep) : definition.Steps,
            Variables = definition.Variables is { Count: 0 } ? null : definition.Variables,
            Inputs = definition.Inputs is { Count: 0 } ? null : definition.Inputs,
        };

    private static List<NodePosition> Walk(SequenceDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        List<NodePosition> positions = [];

        void Add(IReadOnlyList<SequenceStep?>? steps, Guid? parentId, string? body, int depth)
        {
            for (int sibling = 0; sibling < (steps?.Count ?? 0); sibling++)
            {
                if (steps![sibling] is not { } step)
                {
                    continue;
                }

                positions.Add(new NodePosition(step, parentId, body, sibling, positions.Count, depth));

                foreach (StepBody inside in step.Bodies)
                {
                    Add(inside.Steps, step.Id, inside.Name, depth + 1);
                }
            }
        }

        Add(definition.Steps, null, null, 0);

        return positions;
    }

    private static SequenceStep MapBodies(SequenceStep step, Func<SequenceStep, SequenceStep> map)
    {
        IReadOnlyList<StepBody> bodies = step.Bodies;

        if (bodies.Count == 0)
        {
            return step;
        }

        IReadOnlyList<SequenceStep>[] mapped = [.. bodies.Select(body => Map(body.Steps, map))];

        return mapped.Where((list, index) => !ReferenceEquals(list, bodies[index].Steps)).Any() ? step.WithBodies(mapped) : step;
    }

    private static SequenceStep FoldStep(SequenceStep step)
    {
        SequenceStep folded = step.Shares is { Count: 0 } ? step with { Shares = null } : step;

        if (step.When is { } when && LegacyTests(when) is { } tests)
        {
            IReadOnlyList<StepCondition> conditions = step.Conditions ?? [];

            if (conditions.Count + tests.Count <= LegacyMaxConditions)
            {
                folded = folded with { When = null, Conditions = [.. conditions, .. tests] };
            }
        }

        return folded;
    }

    // The When as version 1 Conditions, if it can be written that way. That works for a test, or an all of tests, with
    // the operators and variables a version 1 agent knows. An empty all always holds, just like no conditions.
    private static List<StepCondition>? LegacyTests(ConditionNode when)
    {
        IReadOnlyList<ConditionNode?>? parts = when switch
        {
            TestCondition test => [test],
            AllCondition all => all.Parts,
            _ => null,
        };

        if (parts is null || !parts.All(IsLegacyTest))
        {
            return null;
        }

        return [.. parts.Cast<TestCondition>().Select(test => new StepCondition(test.Variable, test.Operator, test.Value))];
    }

    private static bool IsLegacyTest(ConditionNode? part) =>
        part is TestCondition { Variable: not null, Value: not null } test && !NeedsTree(test.Variable, test.Operator);

    // An operator after Contains, or a name that a version 1 or 2 agent doesn't know. A missing name needs nothing
    // new, because every version refuses it.
    private static bool NeedsTree(string? variable, ConditionOperator op) =>
        op > ConditionOperator.Contains || (variable is not null && !MachineVariableNames.All.Contains(variable, StringComparer.Ordinal));
}
