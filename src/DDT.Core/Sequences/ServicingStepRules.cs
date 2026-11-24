// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using DDT.Contracts.Messages;
using DDT.Contracts.Sequences;

namespace DDT.Core.Sequences;

// The rules of a Service packages step. The phase rule is the one every Windows PE step has; only the server knows
// whether a package is in the library.
internal static class ServicingStepRules
{
    public static void Check(ServicePackagesStep step, StepPlace place, StepProblems problems)
    {
        if (!place.Before.MustHave(Happened.ImageApplied))
        {
            problems.Add(null, ServerMessages.SequenceServicingBeforeImage.With());
        }

        IReadOnlyList<Guid> packages = step.Packages ?? [];

        if (packages.Count == 0 && !step.MatchingUpdates)
        {
            problems.Add("packages", ServerMessages.SequenceServicingChoose.With());
        }
        else if (packages.Count > ServicePackagesStep.MaxPackages)
        {
            problems.Add("packages", ServerMessages.SequenceServicingTooMany.With("max", ServicePackagesStep.MaxPackages));
        }

        HashSet<Guid> seen = [];

        for (int index = 0; index < packages.Count; index++)
        {
            if (!seen.Add(packages[index]))
            {
                problems.Add(string.Create(CultureInfo.InvariantCulture, $"packages[{index}]"), ServerMessages.SequenceServicingRepeated.With());
            }
        }
    }
}
