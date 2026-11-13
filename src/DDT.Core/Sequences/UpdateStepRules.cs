// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using DDT.Contracts.Messages;
using DDT.Contracts.Sequences;

namespace DDT.Core.Sequences;

// The rules of a Windows Update step. The phase rule is the one every Windows step has.
internal static class UpdateStepRules
{
    public static void Check(WindowsUpdateStep step, StepProblems problems)
    {
        if (step.AllowedRestarts is < 1 or > WindowsUpdateStep.MaxRestarts)
        {
            problems.Add("restartLimit", ServerMessages.SequenceUpdatesRestarts.With("max", WindowsUpdateStep.MaxRestarts));
        }

        IReadOnlyList<string?> kbs = step.ExcludedKbs ?? [];

        if (kbs.Count > WindowsUpdateStep.MaxExcludedKbs)
        {
            problems.Add("excludedKbs", ServerMessages.SequenceUpdatesTooManyKbs.With("max", WindowsUpdateStep.MaxExcludedKbs));
        }

        HashSet<string> seen = new(StringComparer.Ordinal);

        for (int index = 0; index < kbs.Count; index++)
        {
            string at = string.Create(CultureInfo.InvariantCulture, $"excludedKbs[{index}]");

            if (KbNumbers.Normalize(kbs[index]) is not { } kb)
            {
                problems.Add(at, ServerMessages.SequenceUpdatesKb.With());
            }
            else if (!seen.Add(kb))
            {
                problems.Add(at, ServerMessages.SequenceUpdatesKbRepeated.With());
            }
        }
    }
}
