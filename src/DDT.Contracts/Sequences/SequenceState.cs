// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Sequences;

// The resumable state of a run, saved after every change: the blob alone is enough to resume after a restart.
// Steps holds one entry per step of the frozen Definition, in its order. Variables are run variables that steps
// output, such as partition ids. Later formats only add members.
public sealed record SequenceState(
    int Format,
    Guid RunId,
    SequenceDefinition Definition,
    SequencePhase Phase,
    int NextIndex,
    IReadOnlyList<StepRunState> Steps,
    IReadOnlyDictionary<string, string> Variables)
{
    public const int CurrentFormat = 1;
}
