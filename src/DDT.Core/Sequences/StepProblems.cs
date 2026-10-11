// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;
using DDT.Contracts.Sequences;

namespace DDT.Core.Sequences;

// Collects the problems, warnings and notes of one node, under the node's id. It stays silent while SequencePaths
// walks a repeat's body just to find the state an iteration starts with.
internal sealed class StepProblems(Guid? stepId, SequenceFindings findings, bool silent)
{
    public void Add(string? field, ServerMessage message)
    {
        if (!silent)
        {
            findings.Problems.Add(SequenceProblem.From(stepId, field, message));
        }
    }

    public void Warn(string? field, ServerMessage message)
    {
        if (!silent)
        {
            findings.Warnings.Add(SequenceProblem.From(stepId, field, message));
        }
    }

    public void Note(string? field, ServerMessage message)
    {
        if (!silent)
        {
            findings.Notes.Add(SequenceProblem.From(stepId, field, message));
        }
    }
}
