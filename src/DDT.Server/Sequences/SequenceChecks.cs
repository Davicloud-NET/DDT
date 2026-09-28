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
using DDT.Core.Templates;
using DDT.Server.Accounts;
using DDT.Server.Deployments;
using DDT.Server.Images;
using DDT.Server.Packages;

namespace DDT.Server.Sequences;

// SequenceValidator's rules plus what only the server can check: the library, its settings, and culture names,
// which the agent cannot check because it runs without globalization data. Every node of the tree is checked, on every
// branch of every IF, since any of them may run.
public static class SequenceChecks
{
    // Parts of a name that say its value is a password or another secret, ignoring case.
    private static readonly string[] s_secretNames = ["password", "passwd", "passwort", "kennwort", "pwd", "secret", "token"];

    public static SequenceValidation Check(SequenceDefinition definition, SequenceReferences references)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(references);

        SequenceAnalysis analysis = SequenceValidator.Analyse(definition);
        List<SequenceProblem> problems = [.. analysis.Problems];
        List<SequenceProblem> warnings = [.. analysis.Warnings];
        bool addsAdministrator = false;

        foreach (SequenceStep step in SequenceTree.Nodes(definition))
        {
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
                            "starting",
                            BootCapabilities.NotStartingChoice(written.BootCapability),
                            "detail",
                            written.BootDetail ?? ""));
                    }

                    break;
                case WriteCloudInitSeedStep seed:
                    CheckPlaceholders(seed, SeedValueNames(definition, references), Warn);
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
                case SetVariableStep set when SecretValue(set.Variable, set.Value):
                    Warn("value", ServerMessages.SequenceSecretValueWarning.With("name", set.Variable));
                    break;
            }

            CheckShareHosts(step, Warn);
        }

        CheckDeclaredSecrets(definition, warnings);

        // Some path reaches Windows, and no answer file on any path adds the administrator.
        if (InWindows(analysis.NodePhases) && !addsAdministrator)
        {
            warnings.Add(SequenceProblem.From(null, null, ServerMessages.SequenceNoAdministratorWarning.With()));
        }

        problems.AddRange(SequenceAccountChecks.Check(definition, references));

        // The validator leaves the names only rules and machine roles can give a value to the server, which knows them. A
        // name nothing gives one is most likely a slip, but a rule added later may still give it one.
        warnings.AddRange(analysis.ValueNames
            .Where(name => !references.ValueNames.Contains(name))
            .Select(name => SequenceProblem.From(null, null, ServerMessages.SequenceValueUndefined.With("name", name))));

        return new SequenceValidation(problems, warnings);
    }

    // The phase of each step at the top, as a list of steps shows them. NodePhases has every node of the tree.
    public static IReadOnlyList<SequencePhase> Phases(SequenceDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return [.. definition.Steps.Select((_, index) => SequencePhases.Of(definition, index))];
    }

    // The phases each node may run in, in the order of SequenceTree.Nodes: more than one where it depends on the path.
    public static IReadOnlyList<NodePhase> NodePhases(SequenceDefinition definition) => SequenceValidator.Analyse(definition).NodePhases;

    // Whether some path through the sequence goes on in Windows.
    public static bool ContinuesInWindows(SequenceDefinition definition) => InWindows(NodePhases(definition));

    // Why a sequence needs a computer name, or null when it needs none: it joins the domain under it, its cloud-init
    // seed names the machine with it, or it declares the ComputerName variable, whose value names the machine. A join or
    // a seed on any branch counts, since any branch may run.
    public static ServerMessage? ComputerNameUse(SequenceDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        IReadOnlyList<SequenceStep> nodes = SequenceTree.Nodes(definition);

        if (nodes.Any(node => node is JoinDomainStep))
        {
            return ServerMessages.DeploymentJoinsDomainUnderName.With();
        }

        if (nodes.OfType<WriteCloudInitSeedStep>().Any(seed => SeedTexts(seed)
            .SelectMany(text => CloudInitTemplate.Placeholders(text.Text ?? ""))
            .Any(placeholder => CloudInitTemplate.Known(placeholder) == MachineVariableNames.ComputerName)))
        {
            return ServerMessages.DeploymentSeedNamesMachine.With();
        }

        return (definition.Variables ?? []).Any(variable =>
            string.Equals(variable?.Name, MachineVariableNames.ComputerName, StringComparison.OrdinalIgnoreCase))
            ? ServerMessages.SequenceNamesMachineWithValue.With()
            : null;
    }

    // The raw disk image a sequence writes, if it writes one that is in the library.
    public static Image? RawImage(SequenceDefinition definition, SequenceReferences references)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(references);

        return SequenceTree.Nodes(definition).OfType<WriteRawImageStep>()
            .Select(step => references.Images.GetValueOrDefault(step.ImageId))
            .FirstOrDefault(image => image is { Kind: ImageKind.RawDisk });
    }

    private static bool InWindows(IReadOnlyList<NodePhase> nodes) => nodes.Any(node => node.Phases.Contains(SequencePhase.Windows));

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

    // Besides the machine's names, a seed may use the run's values, as the agent fills them in: the sequence's variables
    // and the answers to its inputs, what rules and machine roles set, and the deployment defaults. An Account input's
    // answer is never a value.
    private static List<string> SeedValueNames(SequenceDefinition definition, SequenceReferences references)
    {
        IEnumerable<string?> names =
        [
            .. (definition.Variables ?? []).Select(variable => variable?.Name),
            .. (definition.Inputs ?? []).Where(input => input is { Kind: not InputKind.Account }).Select(input => input!.Name),
            .. references.ValueNames.Order(StringComparer.OrdinalIgnoreCase),
        ];

        return
        [
            .. names
                .OfType<string>()
                .Where(name => name.Length > 0 && CloudInitTemplate.Known(name) is null)
                .Distinct(StringComparer.OrdinalIgnoreCase),
        ];
    }

    private static void CheckPlaceholders(WriteCloudInitSeedStep seed, IReadOnlyList<string> valueNames, Action<string?, ServerMessage> warn)
    {
        string known = string.Join(", ", CloudInitTemplate.Names.Concat(valueNames).Select(name => $"{{{{{name}}}}}"));

        foreach ((string field, string? text) in SeedTexts(seed))
        {
            string[] unknown =
            [
                .. CloudInitTemplate.Placeholders(text ?? "").Where(placeholder =>
                    CloudInitTemplate.Known(placeholder) is null && !valueNames.Contains(placeholder, StringComparer.OrdinalIgnoreCase)),
            ];

            if (unknown.Length > 0)
            {
                string named = string.Join(", ", unknown.Select(name => $"{{{{{name}}}}}"));
                warn(field, ServerMessages.SequenceUnknownPlaceholders.With("count", unknown.Length, "named", named, "known", known));
            }
        }
    }

    // Every signed-in user, Viewers too, reads a sequence's values, so a password written into one is no secret. Only a
    // value written out counts: one made of other values, such as {{Token}}, holds none itself.
    private static bool SecretValue(string? name, string? value) =>
        name is not null
        && s_secretNames.Any(part => name.Contains(part, StringComparison.OrdinalIgnoreCase))
        && !string.IsNullOrWhiteSpace(value)
        && ValueTemplate.Parse(value).Placeholders.Count == 0;

    // The defaults of the sequence's variables and inputs. An Account input keeps its answer apart, and has no default.
    private static void CheckDeclaredSecrets(SequenceDefinition definition, List<SequenceProblem> warnings)
    {
        void Check(string field, string? name, string? value)
        {
            if (SecretValue(name, value))
            {
                warnings.Add(SequenceProblem.From(null, field, ServerMessages.SequenceSecretValueWarning.With("name", name!)));
            }
        }

        IReadOnlyList<VariableDeclaration?> variables = definition.Variables ?? [];

        for (int index = 0; index < variables.Count; index++)
        {
            Check(string.Create(CultureInfo.InvariantCulture, $"variables[{index}].default"), variables[index]?.Name, variables[index]?.Default);
        }

        IReadOnlyList<InputDeclaration?> inputs = definition.Inputs ?? [];

        for (int index = 0; index < inputs.Count; index++)
        {
            if (inputs[index] is { Kind: not InputKind.Account } input)
            {
                Check(string.Create(CultureInfo.InvariantCulture, $"inputs[{index}].default"), input.Name, input.Default);
            }
        }
    }

    // A server named by its address gets no Kerberos ticket, so the account's password goes to it by NTLM, which a
    // machine in the middle can relay to another server. Only a host written out can be told; one made of values is
    // known when the step runs.
    private static void CheckShareHosts(SequenceStep step, Action<string?, ServerMessage> warn)
    {
        IReadOnlyList<ShareConnection?> shares = step.Shares ?? [];

        for (int index = 0; index < shares.Count; index++)
        {
            if (AccountRules.WrittenHost(shares[index]?.Path) is { } host && ValueTemplate.Parse(host).Placeholders.Count == 0 && IsAddress(host))
            {
                warn(
                    string.Create(CultureInfo.InvariantCulture, $"shares[{index}].path"),
                    ServerMessages.SequenceShareHostAddressWarning.With("host", host));
            }
        }
    }

    // An IPv4 address, or an IPv6 address written as a name Windows takes in a share path.
    private static bool IsAddress(string host) =>
        Uri.CheckHostName(host) == UriHostNameType.IPv4 || host.EndsWith(".ipv6-literal.net", StringComparison.OrdinalIgnoreCase);

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
        if (Value(step.TimeZone) is { } timeZone && !WindowsSettings.IsTimeZone(timeZone))
        {
            add("timeZone", ServerMessages.SequenceTimeZone.With("timeZone", timeZone));
        }

        if (Value(step.Locale) is { } locale && !WindowsSettings.IsLocale(locale))
        {
            add("locale", ServerMessages.SequenceLocale.With("locale", locale));
        }

        if (Value(step.Keyboard) is { } keyboard && !WindowsSettings.IsKeyboard(keyboard))
        {
            add("keyboard", ServerMessages.SequenceKeyboard.With("keyboard", keyboard));
        }

        if (step.LocalAdministrator && !references.LocalAdministratorConfigured)
        {
            add("localAdministrator", ServerMessages.SequenceNoLocalAdministrator.With());
        }
    }

    // A join with an account joins that account's domain, so only a join without one needs the configured domain.
    private static void CheckJoin(JoinDomainStep step, SequenceReferences references, Action<string?, ServerMessage> add)
    {
        if (step.Account is null && !references.DomainConfigured)
        {
            add(null, ServerMessages.SequenceNoDomain.With());
        }

        if (Value(step.OrganizationalUnit) is { } organizationalUnit
            && DeploymentOptionsValidation.OrganizationalUnitMessage(organizationalUnit) is { } problem)
        {
            add("organizationalUnit", problem);
        }
    }

    // A setting as it is written, or null when it is empty or a template, whose values are checked when the run takes
    // them, as the answer file and the join are made.
    private static string? Value(string? setting) =>
        string.IsNullOrWhiteSpace(setting) || ValueTemplate.Parse(setting).Placeholders.Count > 0 ? null : setting.Trim();
}
