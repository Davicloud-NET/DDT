// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;
using DDT.Contracts.Sequences;

namespace DDT.Core.Sequences;

// The rules of the two BitLocker steps. The phase rule is the one every Windows PE or Windows step has.
internal static class BitLockerStepRules
{
    // On the empty partition, so Apply image writes onto a volume that is already encrypting.
    public static void Check(PreprovisionBitLockerStep step, StepPlace place, StepProblems problems)
    {
        if (!place.Partitioned)
        {
            problems.Add(null, ServerMessages.SequenceBitLockerBeforePartition.With());
        }

        if (place.Before.MayHave(Happened.ImageApplied))
        {
            problems.Add(null, ServerMessages.SequenceBitLockerAfterImage.With());
        }

        if (place.Again(Happened.BitLockerPreprovisioned))
        {
            problems.Add(null, ServerMessages.SequenceOneBitLockerPreprovision.With());
        }
    }

    public static void Check(EnableBitLockerStep step, StepPlace place, StepProblems problems)
    {
        if (place.Again(Happened.BitLockerEnabled))
        {
            problems.Add(null, ServerMessages.SequenceOneBitLocker.With());
        }

        if (step.WaitMinutes is < 1 or > EnableBitLockerStep.MaxWaitMinutes)
        {
            problems.Add("waitMinutes", ServerMessages.SequenceBitLockerWaitMinutes.With("max", EnableBitLockerStep.MaxWaitMinutes));
        }
    }
}
