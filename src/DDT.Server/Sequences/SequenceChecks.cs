// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using DDT.Contracts.Images;
using DDT.Contracts.Packages;
using DDT.Contracts.Sequences;
using DDT.Core.CloudInit;
using DDT.Core.Sequences;
using DDT.Core.Unattend;
using DDT.Server.Deployments;
using DDT.Server.Images;
using DDT.Server.Packages;

namespace DDT.Server.Sequences;

// SequenceValidator's rules plus what only the server can check: the library, its settings, and culture names,
// which the agent cannot check because it runs without globalization data.
public static class SequenceChecks
{
    private const string SecureBootAdvice =
        "Turn Secure Boot off in the firmware of the machines it goes to, or enroll your own key. Assigning the sequence then asks " +
        "to allow it.";

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

            void Warn(string? field, string message) => warnings.Add(new SequenceProblem(stepId, field, message));

            switch (step)
            {
                case ApplyImageStep apply:
                    CheckImage(apply.ImageId, ImageKind.Wim, references, Add);
                    break;
                case WriteRawImageStep raw:
                    CheckImage(raw.ImageId, ImageKind.RawDisk, references, Add);

                    if (references.Images.TryGetValue(raw.ImageId, out Image? written)
                        && written is { Kind: ImageKind.RawDisk, BootCapability: not ImageBootCapability.SecureBootOk })
                    {
                        Warn("imageId", $"{written.Name} {BootCapabilities.NotStarting(written.BootCapability)} with Secure Boot on. {written.BootDetail} {SecureBootAdvice}");
                    }

                    break;
                case WriteCloudInitSeedStep seed:
                    CheckPlaceholders(seed, Warn);
                    break;
                case WriteUnattendStep unattend:
                    CheckUnattend(unattend, references, Add);
                    addsAdministrator |= unattend.LocalAdministrator;
                    break;
                case JoinDomainStep join:
                    CheckJoin(join, references, Add);
                    break;
                case RunScriptStep { PackageId: { } packageId }:
                    CheckPackage(packageId, references, Add);
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

    // Why a sequence needs a computer name, or null when it needs none: it joins the domain under it, or its cloud-init
    // seed names the machine with it.
    public static string? ComputerNameUse(SequenceDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (definition.Steps.Any(step => step is JoinDomainStep))
        {
            return "The sequence joins the machine to the domain under this name.";
        }

        return definition.Steps.OfType<WriteCloudInitSeedStep>().Any(seed => SeedTexts(seed)
            .SelectMany(text => CloudInitTemplate.Placeholders(text.Text ?? ""))
            .Any(placeholder => CloudInitTemplate.Known(placeholder) == MachineVariableNames.ComputerName))
            ? "The sequence's cloud-init seed gives the machine this name."
            : null;
    }

    // The raw disk image a sequence writes, if it writes one that is in the library.
    public static Image? RawImage(SequenceDefinition definition, SequenceReferences references)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(references);

        return definition.Steps.OfType<WriteRawImageStep>()
            .Select(step => references.Images.GetValueOrDefault(step.ImageId))
            .FirstOrDefault(image => image is { Kind: ImageKind.RawDisk });
    }

    private static void CheckImage(Guid imageId, ImageKind kind, SequenceReferences references, Action<string?, string> add)
    {
        if (imageId == Guid.Empty)
        {
            add("imageId", kind == ImageKind.Wim ? "Choose the image to apply." : "Choose the raw disk image to write.");
        }
        else if (!references.Images.TryGetValue(imageId, out Image? image))
        {
            add("imageId", "The image is no longer in the library. Choose another image.");
        }
        else if (image.Kind != kind)
        {
            add(
                "imageId",
                kind == ImageKind.Wim
                    ? $"{image.Name} is a raw disk image, which a Write raw disk image step writes. Choose a Windows image."
                    : $"{image.Name} is a Windows image, which an Apply image step applies. Choose a raw disk image.");
        }
        else if (DeploymentService.NotDeployable(image) is { } reason)
        {
            add("imageId", reason);
        }
    }

    private static void CheckPlaceholders(WriteCloudInitSeedStep seed, Action<string?, string> warn)
    {
        string known = string.Join(", ", CloudInitTemplate.Names.Select(name => $"{{{{{name}}}}}"));

        foreach ((string field, string? text) in SeedTexts(seed))
        {
            string[] unknown = [.. CloudInitTemplate.Placeholders(text ?? "").Where(placeholder => CloudInitTemplate.Known(placeholder) is null)];

            if (unknown.Length > 0)
            {
                string named = string.Join(", ", unknown.Select(name => $"{{{{{name}}}}}"));
                warn(
                    field,
                    unknown.Length == 1
                        ? $"{named} is not one of DDT's placeholders, so it stays as it is. DDT fills in {known}."
                        : $"{named} are not DDT's placeholders, so they stay as they are. DDT fills in {known}.");
            }
        }
    }

    private static IEnumerable<(string Field, string? Text)> SeedTexts(WriteCloudInitSeedStep seed) =>
        [("metaData", seed.MetaData), ("userData", seed.UserData), ("networkConfig", seed.NetworkConfig)];

    private static void CheckPackage(Guid packageId, SequenceReferences references, Action<string?, string> add)
    {
        if (!references.Packages.TryGetValue(packageId, out Package? package))
        {
            add("packageId", "The package is no longer in the library. Choose another package, or none.");
        }
        else if (package.Kind != PackageKind.Files)
        {
            add("packageId", $"{package.Name} is a driver package. A script runs with a files package.");
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
