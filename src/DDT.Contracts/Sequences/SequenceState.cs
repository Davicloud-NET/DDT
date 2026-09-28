// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization;

namespace DDT.Contracts.Sequences;

// The resumable state of a run, saved after every change: the blob alone is enough to resume after a restart.
// Steps holds one entry per step of the frozen Definition, in its order. Variables are run variables that steps
// output, such as partition ids. Later formats only add members.
//
// Format 2 walks the tree: Steps holds one entry per node in pre-order (SequenceTree.Nodes), which for a flat document
// is the same list as Format 1, and Cursor, not NextIndex, says where the run goes on. The engine decides which format
// it writes; the rule is Format 1 while the definition's version is 1 or 2, so an older agent still resumes a flat run.
public sealed record SequenceState(
    int Format,
    Guid RunId,
    SequenceDefinition Definition,
    SequencePhase Phase,
    int NextIndex,
    IReadOnlyList<StepRunState> Steps,
    IReadOnlyDictionary<string, string> Variables,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] NodeCursor? Cursor = null)
{
    public const int CurrentFormat = 1;

    public const int TreeFormat = 2;
}
