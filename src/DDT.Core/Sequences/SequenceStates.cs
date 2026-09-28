// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;

namespace DDT.Core.Sequences;

public static class SequenceStates
{
    // A Format 2 state continues at its cursor. Agents of versions 1 and 2 refuse a NextIndex below 0. So none of them
    // runs a tree's state from an index that means nothing to it.
    public const int NoNextIndex = -1;

    // The highest version a definition may need and still start as Format 1.
    private const int FlatVersion = 2;

    // Starts in Windows PE with one entry per node, in pre-order. A definition that a version 1 or 2 agent could run
    // starts as Format 1, so an older agent can still resume it. One that needs the tree starts as Format 2.
    public static SequenceState Start(Guid runId, SequenceDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        IReadOnlyList<SequenceStep> nodes = SequenceTree.Nodes(definition);
        StepRunState[] steps = [.. nodes.Select(node => new StepRunState(node.Id, StepState.Pending, null))];
        Dictionary<string, string> variables = new(StringComparer.Ordinal);

        if (definition.RequiredVersion() <= FlatVersion)
        {
            return new SequenceState(SequenceState.CurrentFormat, runId, definition, SequencePhase.WindowsPE, 0, steps, variables);
        }

        return new SequenceState(
            SequenceState.TreeFormat,
            runId,
            definition,
            SequencePhase.WindowsPE,
            NoNextIndex,
            steps,
            variables,
            nodes.Count > 0 ? new NodeCursor(nodes[0].Id, false) : null);
    }

    // Converts a Format 1 state for the tree walk. The cursor goes to Steps[NextIndex], or null past the end. Every
    // step that isn't Pending gets a pass of 1, since a flat run enters each step once. Format 2 comes back unchanged.
    public static SequenceState Upgrade(SequenceState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.Format >= SequenceState.TreeFormat)
        {
            return state;
        }

        IReadOnlyList<SequenceStep> nodes = SequenceTree.Nodes(state.Definition);

        return state with
        {
            Format = SequenceState.TreeFormat,
            NextIndex = NoNextIndex,
            Steps = [.. state.Steps.Select(step => step.State == StepState.Pending ? step : step with { Pass = Math.Max(step.Pass, 1) })],
            Cursor = state.NextIndex >= 0 && state.NextIndex < nodes.Count ? new NodeCursor(nodes[state.NextIndex].Id, false) : null,
        };
    }
}
