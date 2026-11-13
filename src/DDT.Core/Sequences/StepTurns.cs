// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;

namespace DDT.Core.Sequences;

// The turns of the steps that go on after a restart, at most one per step.
internal sealed class StepTurns(IReadOnlyList<StepTurn>? turns)
{
    private readonly List<StepTurn> _turns = [.. (turns ?? []).Where(turn => turn is not null)];

    // Null for an entry from an earlier visit.
    public StepTurn? Of(Guid stepId, int pass) => _turns.Find(turn => turn.StepId == stepId && turn.Pass == pass);

    public void Keep(StepTurn turn)
    {
        Drop(turn.StepId);
        _turns.Add(turn);
    }

    public void Drop(Guid stepId) => _turns.RemoveAll(turn => turn.StepId == stepId);

    public IReadOnlyList<StepTurn>? Snapshot() => _turns.Count == 0 ? null : [.. _turns];
}
