// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;
using DDT.Contracts.Sequences;

namespace DDT.Core.Sequences;

// Where the checks of one node report, under the node's id. Silent while SequencePaths walks a repeat's body only to
// find the states a time round starts with.
internal sealed class StepProblems(Guid? stepId, List<SequenceProblem> problems, List<SequenceProblem> warnings, bool silent)
{
    public void Add(string? field, ServerMessage message)
    {
        if (!silent)
        {
            problems.Add(SequenceProblem.From(stepId, field, message));
        }
    }

    public void Warn(string? field, ServerMessage message)
    {
        if (!silent)
        {
            warnings.Add(SequenceProblem.From(stepId, field, message));
        }
    }
}
