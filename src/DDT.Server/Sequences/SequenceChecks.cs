// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using DDT.Contracts.Images;
using DDT.Contracts.Messages;
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

            void Add(string? field, ServerMessage message) => problems.Add(SequenceProblem.From(stepId, field, message));

            void Warn(string? field, ServerMessage message) => warnings.Add(SequenceProblem.From(stepId, field, message));

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
                        Warn("imageId", ServerMessages.SequenceRawImageNotStarting.With(
                            "image",
                            written.Name,
                            "capability",
                            written.BootCapability?.ToString() ?? nameof(ImageBootCapability.Unknown),
                            "detail",
                            written.BootDetail ?? ""));
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
            warnings.Add(SequenceProblem.From(null, null, ServerMessages.SequenceNoAdministratorWarning.With()));
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
    public static ServerMessage? ComputerNameUse(SequenceDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (definition.Steps.Any(step => step is JoinDomainStep))
        {
            return ServerMessages.DeploymentJoinsDomainUnderName.With();
        }

        return definition.Steps.OfType<WriteCloudInitSeedStep>().Any(seed => SeedTexts(seed)
            .SelectMany(text => CloudInitTemplate.Placeholders(text.Text ?? ""))
            .Any(placeholder => CloudInitTemplate.Known(placeholder) == MachineVariableNames.ComputerName))
            ? ServerMessages.DeploymentSeedNamesMachine.With()
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

    private static void CheckImage(Guid imageId, ImageKind kind, SequenceReferences references, Action<string?, ServerMessage> add)
    {
        if (imageId == Guid.Empty)
        {
            add("imageId", (kind == ImageKind.Wim ? ServerMessages.SequenceChooseImage : ServerMessages.SequenceChooseRawImage).With());
        }
        else if (!references.Images.TryGetValue(imageId, out Image? image))
        {
            add("imageId", ServerMessages.SequenceImageGone.With());
        }
        else if (image.Kind != kind)
        {
            add(
                "imageId",
                (kind == ImageKind.Wim ? ServerMessages.SequenceImageIsRaw : ServerMessages.SequenceImageIsWindows).With("image", image.Name));
        }
        else if (DeploymentService.NotDeployable(image) is { } reason)
        {
            add("imageId", reason);
        }
    }

    private static void CheckPlaceholders(WriteCloudInitSeedStep seed, Action<string?, ServerMessage> warn)
    {
        string known = string.Join(", ", CloudInitTemplate.Names.Select(name => $"{{{{{name}}}}}"));

        foreach ((string field, string? text) in SeedTexts(seed))
        {
            string[] unknown = [.. CloudInitTemplate.Placeholders(text ?? "").Where(placeholder => CloudInitTemplate.Known(placeholder) is null)];

            if (unknown.Length > 0)
            {
                string named = string.Join(", ", unknown.Select(name => $"{{{{{name}}}}}"));
                warn(field, ServerMessages.SequenceUnknownPlaceholders.With("count", unknown.Length, "named", named, "known", known));
            }
        }
    }

    private static IEnumerable<(string Field, string? Text)> SeedTexts(WriteCloudInitSeedStep seed) =>
        [("metaData", seed.MetaData), ("userData", seed.UserData), ("networkConfig", seed.NetworkConfig)];

    private static void CheckPackage(Guid packageId, SequenceReferences references, Action<string?, ServerMessage> add)
    {
        if (!references.Packages.TryGetValue(packageId, out Package? package))
        {
            add("packageId", ServerMessages.SequencePackageGone.With());
        }
        else if (package.Kind != PackageKind.Files)
        {
            add("packageId", ServerMessages.SequencePackageIsDrivers.With("package", package.Name));
        }
    }

    private static void CheckUnattend(WriteUnattendStep step, SequenceReferences references, Action<string?, ServerMessage> add)
    {
        if (Value(step.TimeZone) is { } timeZone && !WindowsTimeZones.IsValidId(timeZone))
        {
            add("timeZone", ServerMessages.SequenceTimeZone.With("timeZone", timeZone));
        }

        if (Value(step.Locale) is { } locale && !IsSpecificCulture(locale))
        {
            add("locale", ServerMessages.SequenceLocale.With("locale", locale));
        }

        if (Value(step.Keyboard) is { } keyboard && !keyboard.Split(';').All(IsInputLocale))
        {
            add("keyboard", ServerMessages.SequenceKeyboard.With("keyboard", keyboard));
        }

        if (step.LocalAdministrator && !references.LocalAdministratorConfigured)
        {
            add("localAdministrator", ServerMessages.SequenceNoLocalAdministrator.With());
        }
    }

    private static void CheckJoin(JoinDomainStep step, SequenceReferences references, Action<string?, ServerMessage> add)
    {
        if (!references.DomainConfigured)
        {
            add(null, ServerMessages.SequenceNoDomain.With());
        }

        if (Value(step.OrganizationalUnit) is { } organizationalUnit
            && DeploymentOptionsValidation.OrganizationalUnitMessage(organizationalUnit) is { } problem)
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
