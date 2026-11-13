// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;
using DDT.Contracts.Sequences;
using DDT.Core.Templates;
using DDT.Core.Unattend;

namespace DDT.Core.Sequences;

// The rules of a Write the answer file step's own settings. A template is checked when the run fills it in.
internal static class UnattendStepRules
{
    public static void Check(WriteUnattendStep step, SequenceNames names, StepProblems problems)
    {
        names.CheckTemplate(step.TimeZone, "timeZone", problems.Add);
        names.CheckTemplate(step.Locale, "locale", problems.Add);
        names.CheckTemplate(step.Keyboard, "keyboard", problems.Add);
        names.CheckTemplate(step.UiLanguage, "uiLanguage", problems.Add);
        names.CheckTemplate(step.Organization, "organization", problems.Add);
        names.CheckTemplate(step.Owner, "owner", problems.Add);

        if (Literal(step.UiLanguage) is { } language && !WindowsSetupValues.IsUiLanguage(language))
        {
            problems.Add("uiLanguage", ServerMessages.SequenceUiLanguage.With("value", language));
        }

        CheckLength(step.Organization, "organization", problems);
        CheckLength(step.Owner, "owner", problems);
    }

    private static void CheckLength(string? value, string field, StepProblems problems)
    {
        if (value?.Trim().Length > WindowsSetupValues.MaxRegisteredLength)
        {
            problems.Add(field, ServerMessages.SequenceRegisteredTooLong.With("max", WindowsSetupValues.MaxRegisteredLength));
        }
    }

    private static string? Literal(string? setting) =>
        string.IsNullOrWhiteSpace(setting) || ValueTemplate.Parse(setting).Placeholders.Count > 0 ? null : setting.Trim();
}
