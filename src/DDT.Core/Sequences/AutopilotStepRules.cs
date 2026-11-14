// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;
using DDT.Contracts.Sequences;

namespace DDT.Core.Sequences;

// The rules of a Register with Autopilot step. The phase rule is the one every Windows step has.
internal static class AutopilotStepRules
{
    public static void Check(RegisterAutopilotStep step, StepProblems problems)
    {
        if (step.GroupTag is { } tag && !AutopilotGroupTags.IsValid(tag))
        {
            problems.Add("groupTag", ServerMessages.SequenceAutopilotGroupTag.With("max", RegisterAutopilotStep.MaxGroupTagLength));
        }

        if (step.WaitMinutes is < 1 or > RegisterAutopilotStep.MaxWaitMinutes)
        {
            problems.Add("waitMinutes", ServerMessages.SequenceAutopilotWaitMinutes.With("max", RegisterAutopilotStep.MaxWaitMinutes));
        }
    }
}
