// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using DDT.Contracts.Messages;
using DDT.Contracts.Packages;
using DDT.Contracts.Sequences;
using DDT.Server.Packages;

namespace DDT.Server.Sequences;

// Checks the packages a Service packages step lists against the library. A missing one keeps the sequence a draft.
public static class SequenceWindowsPackageChecks
{
    public static void Check(SequenceDefinition definition, SequenceReferences references, List<SequenceProblem> problems)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(problems);

        foreach (ServicePackagesStep step in SequenceTree.Nodes(definition).OfType<ServicePackagesStep>())
        {
            IReadOnlyList<Guid> packages = step.Packages ?? [];
            Guid? stepId = step.Id == Guid.Empty ? null : step.Id;

            for (int index = 0; index < packages.Count; index++)
            {
                string field = string.Create(CultureInfo.InvariantCulture, $"packages[{index}]");

                if (!references.Packages.TryGetValue(packages[index], out Package? package))
                {
                    problems.Add(SequenceProblem.From(stepId, field, ServerMessages.SequenceServicingPackageGone.With()));
                }
                else if (package.Kind != PackageKind.WindowsPackage)
                {
                    problems.Add(SequenceProblem.From(
                        stepId,
                        field,
                        ServerMessages.SequenceServicingNotWindows.With("package", package.Name)));
                }
            }
        }
    }
}
