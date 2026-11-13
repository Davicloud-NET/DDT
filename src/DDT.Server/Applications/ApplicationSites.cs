// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using DDT.Contracts.Applications;
using DDT.Contracts.Sequences;

namespace DDT.Server.Applications;

// Every place a sequence names an application or a bundle, on every branch, steps first, then inputs. The document
// comes from outside, so empty entries are skipped.
public static class ApplicationSites
{
    public static IReadOnlyList<ApplicationSite> Of(SequenceDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        List<ApplicationSite> sites = [];

        foreach (InstallApplicationsStep step in SequenceTree.Nodes(definition).OfType<InstallApplicationsStep>())
        {
            IReadOnlyList<ApplicationReference?> applications = step.Applications ?? [];

            for (int index = 0; index < applications.Count; index++)
            {
                if (applications[index] is { } reference && reference.Id != Guid.Empty)
                {
                    sites.Add(new ApplicationSite(reference, step, null, At("applications", index)));
                }
            }
        }

        IReadOnlyList<InputDeclaration?> inputs = definition.Inputs ?? [];

        for (int input = 0; input < inputs.Count; input++)
        {
            if (inputs[input] is not { Kind: InputKind.Applications } declared)
            {
                continue;
            }

            IReadOnlyList<ApplicationReference?> offered = declared.Offered ?? [];

            for (int index = 0; index < offered.Count; index++)
            {
                if (offered[index] is { } reference && reference.Id != Guid.Empty)
                {
                    sites.Add(new ApplicationSite(reference, null, declared, $"{At("inputs", input)}.{At("offered", index)}"));
                }
            }
        }

        return sites;
    }

    private static string At(string field, int index) => string.Create(CultureInfo.InvariantCulture, $"{field}[{index}]");
}
