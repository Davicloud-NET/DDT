// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;

namespace DDT.Core.Sequences;

public static class SequencePhases
{
    // A step that doesn't ask for a phase, such as a restart, runs in the phase of the step before it.
    public static SequencePhase Of(SequenceDefinition definition, int index)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, definition.Steps.Count);

        for (int i = index; i >= 0; i--)
        {
            if (definition.Steps[i].RequiredPhase is { } phase)
            {
                return phase;
            }
        }

        return SequencePhase.WindowsPE;
    }
}
