// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;
using DDT.Core.Unattend;
using DDT.Server.Deployments;
using DDT.Server.Images;

namespace DDT.Server.Sequences;

// SequenceValidator's rules plus what only the server can check: the library, its settings, and culture names,
// which the agent cannot check because it runs without globalization data.
public static class SequenceChecks
{
    private const string NoAdministrator =
        "The sequence continues in Windows, but no Write answer file step adds the local administrator. Windows setup then " +
        "stops at the account page, and the sequence waits there until someone finishes it.";

    public static SequenceValidation Check(SequenceDefinition definition, SequenceReferences references)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(references);

        List<SequenceProblem> problems = [.. SequenceValidator.Validate(definition)];
        List<SequenceProblem> warnings = [];
        bool continuesInWindows = false;
        bool addsAdministrator = false;

        for (int index = 0; index < definition.Steps.Count; index++)
        {
            SequenceStep step = definition.Steps[index];
            Guid? stepId = step.Id == Guid.Empty ? null : step.Id;

            void Add(string? field, string message) => problems.Add(new SequenceProblem(stepId, field, message));

            switch (step)
            {
                case ApplyImageStep apply:
                    CheckImage(apply.ImageId, references, Add);
                    break;
                case WriteUnattendStep unattend:
                    CheckUnattend(unattend, references, Add);
                    addsAdministrator |= unattend.LocalAdministrator;
                    break;
                case JoinDomainStep join:
                    CheckJoin(join, references, Add);
                    break;
            }

            continuesInWindows |= SequencePhases.Of(definition, index) == SequencePhase.Windows;
        }

        if (continuesInWindows && !addsAdministrator)
        {
            warnings.Add(new SequenceProblem(null, null, NoAdministrator));
        }

        return new SequenceValidation(problems, warnings);
    }

    public static IReadOnlyList<SequencePhase> Phases(SequenceDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return [.. definition.Steps.Select((_, index) => SequencePhases.Of(definition, index))];
    }

    private static void CheckImage(Guid imageId, SequenceReferences references, Action<string?, string> add)
    {
        if (imageId == Guid.Empty)
        {
            add("imageId", "Choose the image to apply.");
        }
        else if (!references.Images.TryGetValue(imageId, out Image? image))
        {
            add("imageId", "The image is no longer in the library. Choose another image.");
        }
        else if (DeploymentService.NotDeployable(image) is { } reason)
        {
            add("imageId", reason);
        }
    }

    private static void CheckUnattend(WriteUnattendStep step, SequenceReferences references, Action<string?, string> add)
    {
        if (Value(step.TimeZone) is { } timeZone && !WindowsTimeZones.IsValidId(timeZone))
        {
            add("timeZone", $"'{timeZone}' is not a Windows time zone id. Use a name that tzutil /l lists, such as W. Europe Standard Time.");
        }

        if (Value(step.Locale) is { } locale && !IsSpecificCulture(locale))
        {
            add("locale", $"'{locale}' is not a language and region that Windows knows. Use a name such as de-DE.");
        }

        if (Value(step.Keyboard) is { } keyboard && !keyboard.Split(';').All(IsInputLocale))
        {
            add("keyboard", $"'{keyboard}' is not an input locale. Use a name such as de-DE or a code such as 0407:00000407.");
        }

        if (step.LocalAdministrator && !references.LocalAdministratorConfigured)
        {
            add(
                "localAdministrator",
                "No local administrator is configured in DDT:Deployment:LocalAdministrator, so the answer file cannot add one.");
        }
    }

    private static void CheckJoin(JoinDomainStep step, SequenceReferences references, Action<string?, string> add)
    {
        if (!references.DomainConfigured)
        {
            add(null, "No domain is configured in DDT:Deployment:Domain, so the machine has no domain to join. Configure one or remove this step.");
        }

        if (Value(step.OrganizationalUnit) is { } organizationalUnit
            && DeploymentOptionsValidation.OrganizationalUnitProblem(organizationalUnit) is { } problem)
        {
            add("organizationalUnit", problem);
        }
    }

    // A neutral culture such as de names no region, and Windows needs one for its locales.
    private static bool IsSpecificCulture(string name)
    {
        try
        {
            return !CultureInfo.GetCultureInfo(name, predefinedOnly: true).IsNeutralCulture;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }

    // A culture name, a language and keyboard layout pair such as 0407:00000407, or a language and a text service
    // written as two GUIDs, as Windows lists them.
    private static bool IsInputLocale(string part)
    {
        string value = part.Trim();

        if (value.Length > 5 && value[4] == ':' && value[..4].All(char.IsAsciiHexDigit))
        {
            string layout = value[5..];

            return (layout.Length == 8 && layout.All(char.IsAsciiHexDigit))
                || (layout.Length == 76
                    && Guid.TryParseExact(layout[..38], "B", out _)
                    && Guid.TryParseExact(layout[38..], "B", out _));
        }

        return value.Length > 0 && IsSpecificCulture(value);
    }

    private static string? Value(string? setting) => string.IsNullOrWhiteSpace(setting) ? null : setting.Trim();
}
