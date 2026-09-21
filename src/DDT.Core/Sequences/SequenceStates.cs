// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;

namespace DDT.Core.Sequences;

public static class SequenceStates
{
    // Every run starts in Windows PE, before any step.
    public static SequenceState Start(Guid runId, SequenceDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return new SequenceState(
            SequenceState.CurrentFormat,
            runId,
            definition,
            SequencePhase.WindowsPE,
            0,
            [.. definition.Steps.Select(step => new StepRunState(step.Id, StepState.Pending, null))],
            new Dictionary<string, string>(StringComparer.Ordinal));
    }
}
