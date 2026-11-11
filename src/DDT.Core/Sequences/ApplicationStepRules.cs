// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using DDT.Contracts.Applications;
using DDT.Contracts.Messages;
using DDT.Contracts.Sequences;

namespace DDT.Core.Sequences;

// The rules of an Install applications step. Only the server knows whether the applications it names exist.
internal static class ApplicationStepRules
{
    public static void Check(InstallApplicationsStep step, SequenceNames names, StepProblems problems)
    {
        IReadOnlyList<ApplicationReference?> applications = step.Applications ?? [];
        string? variable = string.IsNullOrWhiteSpace(step.Variable) ? null : step.Variable.Trim();

        if (applications.Count == 0 && variable is null)
        {
            problems.Add("applications", ServerMessages.SequenceApplicationsChoose.With());
        }
        else if (applications.Count > SequenceValidator.MaxApplications)
        {
            problems.Add("applications", ServerMessages.SequenceApplicationsTooMany.With("max", SequenceValidator.MaxApplications));
        }

        CheckList(applications, "applications", problems.Add);

        if (variable is not null)
        {
            CheckVariable(variable, names, problems.Add);
        }
    }

    // Each entry names an application or a bundle once.
    public static void CheckList(IReadOnlyList<ApplicationReference?> references, string field, Action<string?, ServerMessage> add)
    {
        HashSet<Guid> seen = [];

        for (int index = 0; index < references.Count; index++)
        {
            string at = string.Create(CultureInfo.InvariantCulture, $"{field}[{index}]");

            if (references[index] is not { } reference || reference.Id == Guid.Empty || !Enum.IsDefined(reference.Kind))
            {
                add(at, ServerMessages.SequenceApplicationEmpty.With());
            }
            else if (!seen.Add(reference.Id))
            {
                add(at, ServerMessages.SequenceApplicationRepeated.With());
            }
        }
    }

    // A name nothing in the sequence declares may be one a rule sets, so it's left for the server's warning.
    private static void CheckVariable(string name, SequenceNames names, Action<string?, ServerMessage> add)
    {
        if (!SequenceNames.IsName(name))
        {
            add("variable", ServerMessages.SequenceValueName.With("max", SequenceValidator.MaxValueNameLength));
        }
        else if (names.IsAccountInput(name))
        {
            add("variable", ServerMessages.SequenceAccountInputAsValue.With("name", name));
        }
        else if (names.ChangesDuringRun(name))
        {
            add("variable", ServerMessages.SequenceApplicationsVariableSetBySteps.With("name", name));
        }
        else if (!names.IsKnown(name))
        {
            names.Tested(name);
        }
    }
}
