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

// SequenceValidator's rules, plus what only the server can check: the library, the settings and culture names. The
// agent can't check culture names without globalization data. Every node on every branch is checked, since any may run.
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
            addsAdministrator |= CheckNode(step, definition, references, problems, warnings);
        }

        CheckDeclaredSecrets(definition, warnings);

        // Some path reaches Windows, and no answer file on any path adds the administrator.
        if (InWindows(analysis.NodePhases) && !addsAdministrator)
        {
            warnings.Add(SequenceProblem.From(null, null, ServerMessages.SequenceNoAdministratorWarning.With()));
        }

        problems.AddRange(SequenceAccountChecks.Check(definition, references));

        // Only rules and machine roles can give some names a value, and only the server knows those, so the validator
        // leaves them to it. A name that nothing gives a value is most likely a typo. But a rule added later may give
        // it one, so it's only a warning.
        warnings.AddRange(analysis.ValueNames
            .Where(name => !references.ValueNames.Contains(name))
            .Select(name => SequenceProblem.From(null, null, ServerMessages.SequenceValueUndefined.With("name", name))));

        return new SequenceValidation(problems, warnings);
    }

    // A step on any branch may run, so a sequence that erases a disk on one branch counts as erasing.
    public static bool Erases(SequenceDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return SequenceTree.Nodes(definition).Any(step => step.ErasesDisk);
    }

    // The phase of each top-level step, as a step list shows them. NodePhases covers every node of the tree.
    public static IReadOnlyList<SequencePhase> Phases(SequenceDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return [.. definition.Steps.Select((_, index) => SequencePhases.Of(definition, index))];
    }

    // The phases each node may run in, in SequenceTree.Nodes order. A node has more than one if it depends on the path.
    public static IReadOnlyList<NodePhase> NodePhases(SequenceDefinition definition) => SequenceValidator.Analyse(definition).NodePhases;

    // Whether some path through the sequence continues in Windows.
    public static bool ContinuesInWindows(SequenceDefinition definition) => InWindows(NodePhases(definition));

    // Why a sequence needs a computer name, or null if it doesn't. It joins the domain under that name, a cloud-init
    // seed names the machine with it, or it declares the ComputerName variable. A join or a seed on any branch counts.
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

    // Returns true if the node is an answer file that adds the local administrator.
    private static bool CheckNode(
        SequenceStep step,
        SequenceDefinition definition,
        SequenceReferences references,
        List<SequenceProblem> problems,
        List<SequenceProblem> warnings)
    {
        Guid? stepId = step.Id == Guid.Empty ? null : step.Id;
        bool addsAdministrator = false;

        void Add(string? field, ServerMessage message) => problems.Add(SequenceProblem.From(stepId, field, message));

        void Warn(string? field, ServerMessage message) => warnings.Add(SequenceProblem.From(stepId, field, message));

        switch (step)
        {
            case ApplyImageStep apply:
                CheckImage(apply.ImageId, ImageKind.Wim, references, Add);
                break;
            case WriteRawImageStep raw:
                CheckImage(raw.ImageId, ImageKind.RawDisk, references, Add);
                WarnNotStarting(raw, references, Warn);
                break;
            case WriteCloudInitSeedStep seed:
                CheckPlaceholders(seed, SeedValueNames(definition, references), Warn);
                break;
            case WriteUnattendStep unattend:
                CheckUnattend(unattend, references, Add);
                addsAdministrator = unattend.LocalAdministrator;
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

        return addsAdministrator;
    }

    private static void WarnNotStarting(WriteRawImageStep raw, SequenceReferences references, Action<string?, ServerMessage> warn)
    {
        if (references.Images.TryGetValue(raw.ImageId, out Image? written)
            && written is { Kind: ImageKind.RawDisk, BootCapability: not ImageBootCapability.SecureBootOk })
        {
            warn("imageId", ServerMessages.SequenceRawImageNotStarting.With(
                "image",
                written.Name,
                "starting",
                BootCapabilities.NotStartingChoice(written.BootCapability),
                "detail",
                written.BootDetail ?? ""));
        }
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
        else if (DeploymentPolicy.NotDeployable(image) is { } reason)
        {
            add("imageId", reason);
        }
    }

    // Besides the machine's names, a seed may use the run's values. Those are variables, answers, what rules and roles
    // set, and the deployment defaults. An Account input's answer is never a value.
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

    // Every signed-in user, Viewers too, can read a sequence's values, so a password written into one isn't secret.
    // Only a literal value counts. One made of other values, such as {{Token}}, holds no secret itself.
    private static bool SecretValue(string? name, string? value) =>
        name is not null
        && s_secretNames.Any(part => name.Contains(part, StringComparison.OrdinalIgnoreCase))
        && !string.IsNullOrWhiteSpace(value)
        && ValueTemplate.Parse(value).Placeholders.Count == 0;

    // Checks the defaults of the sequence's variables and inputs. An Account input keeps its answer separately and has
    // no default.
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

    // A server named by its address gets no Kerberos ticket, so the password goes over NTLM, which a machine in the
    // middle can relay. Only a literal host can be checked here. One made of values is only known when the step runs.
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

    // An IPv4 address, or an IPv6 address written as the ipv6-literal.net name Windows accepts in a share path.
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

    // The setting as written, or null if it's empty or a template. A template's values are checked when the run fills
    // them in, while the answer file and the join are made.
    private static string? Value(string? setting) =>
        string.IsNullOrWhiteSpace(setting) || ValueTemplate.Parse(setting).Placeholders.Count > 0 ? null : setting.Trim();
}
