// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization;

namespace DDT.Contracts.Sequences;

// The resumable state of a run, saved after every change. The blob alone is enough to resume after a restart.
// Later formats only add members.
public sealed record SequenceState(
    int Format,
    Guid RunId,
    SequenceDefinition Definition,
    SequencePhase Phase,
    int NextIndex,
    // One entry per step of the frozen Definition, in its order. Format 2 has one per node in pre-order
    // (SequenceTree.Nodes), which for a flat document is the same list.
    IReadOnlyList<StepRunState> Steps,
    // Run variables that steps output, such as partition ids.
    IReadOnlyDictionary<string, string> Variables,
    // Where a Format 2 run continues. It replaces NextIndex.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] NodeCursor? Cursor = null)
{
    // The engine writes Format 1 while the definition's version is 1 or 2, so an older agent still resumes a flat run.
    public const int CurrentFormat = 1;

    public const int TreeFormat = 2;
}
