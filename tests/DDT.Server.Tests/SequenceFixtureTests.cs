// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using DDT.Contracts;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;
using DDT.Server.Sequences;
using Xunit;

namespace DDT.Server.Tests;

// The web client mirrors the sequence contracts by hand and checks its mirror against these files, so a renamed
// field, kind or enum value fails a web test instead of an editor.
public sealed class SequenceFixtureTests
{
    private static readonly Guid s_imageId = new("0193a4b2-0000-7000-8000-0000000000a1");
    private static readonly Guid s_packageId = new("0193a4b2-0000-7000-8000-0000000000b1");

    [Fact]
    public async Task TheWebFixtureIsTheInstallWindowsTemplate()
    {
        SequenceTemplate template = Assert.Single(
            SequenceTemplates.All(domainConfigured: true, administratorConfigured: true, s_imageId),
            template => template.Key == SequenceTemplates.InstallWindowsKey);
        SequenceTemplate fixedIds = template with
        {
            Definition = template.Definition with
            {
                Steps = [.. template.Definition.Steps.Select((step, index) => step with { Id = StepId(index) })],
            },
        };

        await MatchFixtureAsync("install-windows.sequence.json", fixedIds, "the template");
    }

    // Every kind of step of versions 1 and 2, a script in every phase with every interpreter.
    private static List<SequenceStep> EveryKindOfStep()
    {
        List<SequenceStep> steps = WindowsSteps();

        foreach (SequencePhase phase in Enum.GetValues<SequencePhase>())
        {
            foreach (ScriptInterpreter interpreter in Enum.GetValues<ScriptInterpreter>())
            {
                steps.Add(new RunScriptStep
                {
                    Id = StepId(steps.Count),
                    Name = $"Script {phase} {interpreter}",
                    Phase = phase,
                    Interpreter = interpreter,
                    Script = "exit 0",
                    PackageId = s_packageId,
                    TimeoutMinutes = 30,
                    SuccessExitCodes = [0, 1],
                    RebootExitCodes = [3010, 1641],
                });
            }
        }

        steps.Add(new WriteRawImageStep { Id = StepId(steps.Count), Name = "Write raw", ImageId = s_imageId });
        steps.Add(new WriteCloudInitSeedStep
        {
            Id = StepId(steps.Count),
            Name = "Seed",
            MetaData = "instance-id: \"{{SmbiosUuid}}\"\n",
            UserData = "#cloud-config\nhostname: \"{{ComputerName}}\"\n",
            NetworkConfig = "version: 2\n",
        });

        return steps;
    }

    private static List<SequenceStep> WindowsSteps() =>
        [
            new PartitionStep { Id = StepId(0), Name = "Partition", SystemPartitionMegabytes = 260, RecoveryPartitionMegabytes = 2048 },
            new ApplyImageStep { Id = StepId(1), Name = "Apply", ImageId = s_imageId },
            new InjectDriversStep { Id = StepId(2), Name = "Drivers", RequireMatch = true },
            new WriteUnattendStep
            {
                Id = StepId(3),
                Name = "Answer file",
                TimeZone = "W. Europe Standard Time",
                Locale = "de-AT",
                Keyboard = "0c07:00000407",
                LocalAdministrator = true,
            },
            new JoinDomainStep { Id = StepId(4), Name = "Join", OrganizationalUnit = "OU=Lab,DC=example,DC=org" },
            new RebootStep
            {
                Id = StepId(5),
                Name = "Restart",
                ContinueOnError = true,
                RebootAfter = true,
                Conditions =
                [
                    .. Enum.GetValues<ConditionOperator>()
                        .Where(op => op <= ConditionOperator.Contains)
                        .Select(op => new StepCondition(MachineVariableNames.Model, op, "Latitude 7440")),
                ],
            },
        ];

    // Every kind of step of versions 1 and 2, and every phase, interpreter and condition operator they have: a flat
    // document, as agents of those versions run it. Version 3 is in every-node.sequence.json.
    [Fact]
    public async Task TheWebFixtureHoldsEveryKindOfStep()
    {
        List<SequenceStep> steps = EveryKindOfStep();

        // A kind of these versions added later must be added here too.
        string[] built = [.. steps.Select(step => step.GetType().Name).Distinct().Order(StringComparer.Ordinal)];
        Assert.Equal(Kinds(maximumVersion: 2), built);

        SequenceDefinition definition = new SequenceDefinition(1, steps).Normalised();
        Assert.Equal(2, definition.Version);

        await MatchFixtureAsync("every-step.sequence.json", definition, "the steps built here");
    }

    // Every node, condition, variable and input in a document that still runs, so an editor opens it without problems.
    // Raw image steps live in every-step.sequence.json alone, since a sequence either installs Windows or writes a disk.
    [Fact]
    public async Task TheWebFixtureHoldsEveryNode()
    {
        Guid accountId = new("0193a4b2-0000-7000-8000-0000000000c1");
        List<SequenceStep> steps = EveryNode(Repeat(Choose(accountId)));
        SequenceDefinition definition = new SequenceDefinition(SequenceDefinition.CurrentVersion, steps)
        {
            Variables =
            [
                new VariableDeclaration { Name = "Office", Default = "Standard", Description = "The Office edition.", SetBySteps = true },
                new VariableDeclaration { Name = MachineVariableNames.ComputerName, Default = "PC-{{SerialNumber|alnum|right:12}}" },
            ],
            Inputs = EveryInput(),
        }.Normalised();

        // A kind, condition, operator or input kind added later must be added here too.
        IReadOnlyList<SequenceStep> nodes = SequenceTree.Nodes(definition);
        string[] built = [.. nodes.Select(node => node.GetType().Name).Distinct().Order(StringComparer.Ordinal)];
        string[] conditionKinds =
        [
            .. typeof(ConditionNode).GetCustomAttributes<JsonDerivedTypeAttribute>().Select(kind => kind.DerivedType.Name).Order(StringComparer.Ordinal),
        ];
        string[] conditionsBuilt = [.. nodes.SelectMany(Conditions).Select(condition => condition.GetType().Name).Distinct().Order(StringComparer.Ordinal)];
        InputKind[] inputKinds = [.. definition.Inputs!.Select(input => input.Kind).Distinct().Order()];
        InputAsk[] asks = [.. definition.Inputs!.Select(input => input.AskAt).Distinct().Order()];

        Assert.Equal(
            Kinds(maximumVersion: SequenceDefinition.CurrentVersion).Except([nameof(WriteRawImageStep), nameof(WriteCloudInitSeedStep)]),
            built);
        Assert.Equal(conditionKinds, conditionsBuilt);
        Assert.Equal(Enum.GetValues<InputKind>(), inputKinds);
        Assert.Equal(Enum.GetValues<InputAsk>(), asks);
        Assert.Equal(SequenceDefinition.CurrentVersion, definition.Version);
        Assert.Empty(SequenceValidator.Analyse(definition).Problems);

        await MatchFixtureAsync("every-node.sequence.json", definition, "the nodes built here");
    }

    private static IfStep Choose(Guid accountId) => new()
    {
        Id = StepId(6),
        Name = "If a ThinkPad",
        Test = new AllCondition
        {
            Parts =
            [
                new TestCondition(MachineVariableNames.Manufacturer, ConditionOperator.Equals, "LENOVO"),
                new TestCondition(MachineVariableNames.MemoryMegabytes, ConditionOperator.GreaterOrEqual, "8192"),
            ],
        },
        Then =
        [
            new RunScriptStep
            {
                Id = StepId(7),
                Name = "Install the dock's firmware",
                Phase = SequencePhase.Windows,
                Interpreter = ScriptInterpreter.PowerShell,
                Script = "exit 0",
                RunAs = new AccountReference(null, "Installer"),
                Shares = [new ShareConnection(@"\\files.corp.example\drivers", new AccountReference(accountId, null))],
            },
        ],
        Else = [new SetVariableStep { Id = StepId(8), Name = "Office for the rest", Variable = "Office", Value = "{{Office|upper}}" }],
    };

    private static RepeatStep Repeat(IfStep choose) => new()
    {
        Id = StepId(5),
        Name = "Until the dock answers",
        Until = new TestCondition(MachineVariableNames.LastExitCode, ConditionOperator.Equals, "0"),
        MaxTimes = 5,
        GoOnAtLimit = true,
        Steps =
        [
            choose,
            new RebootStep
            {
                Id = StepId(9),
                Name = "Restart",
                When = new AllCondition { Parts = [.. Enum.GetValues<ConditionOperator>().Select(Fitting)] },
            },
        ],
    };

    private static List<SequenceStep> EveryNode(RepeatStep repeat) =>
    [
        new PartitionStep { Id = StepId(0), Name = "Partition" },
        new ApplyImageStep { Id = StepId(1), Name = "Apply", ImageId = s_imageId },
        new InjectDriversStep
        {
            Id = StepId(2),
            Name = "Drivers",
            When = new AnyCondition
            {
                Parts =
                [
                    new TestCondition(MachineVariableNames.FriendlyModel, ConditionOperator.Matches, "ThinkPad*"),
                    new NoneCondition { Parts = [new TestCondition(MachineVariableNames.DeviceKind, ConditionOperator.Equals, "Virtual")] },
                ],
            },
        },
        new WriteUnattendStep { Id = StepId(3), Name = "Answer file" },

        // The join comes before the group, so the repeat in it runs in Windows throughout.
        new JoinDomainStep
        {
            Id = StepId(11),
            Name = "Join",
            OrganizationalUnit = "OU={{Office}},DC=corp,DC=example",
            Account = new AccountReference(null, "JoinAccount"),
        },
        new GroupStep
        {
            Id = StepId(4),
            Name = "Configure",
            Conditions = [new StepCondition(MachineVariableNames.Phase, ConditionOperator.Equals, "Windows")],
            ContinueOnError = true,
            Steps = [repeat, new PauseStep { Id = StepId(10), Name = "Check the BIOS", Message = "Check {{ComputerName}}.", ContinueAfterMinutes = 30 }],
        },
    ];

    private static IReadOnlyList<InputDeclaration> EveryInput() =>
    [
        new InputDeclaration { Name = "Owner", Label = "Owner", Help = "Who gets the PC.", Required = true, MaxLength = 64, AskAt = InputAsk.Web },
        new InputDeclaration
        {
            Name = "Office",
            Label = "Office",
            Kind = InputKind.Choice,
            Choices = [new InputChoice("Standard"), new InputChoice("ProPlus", "Professional Plus")],
            Default = "Standard",
        },
        new InputDeclaration
        {
            Name = "Languages",
            Label = "Languages",
            Kind = InputKind.MultiChoice,
            Choices = [new InputChoice("de-DE", "German"), new InputChoice("en-US", "English")],
            AskAt = InputAsk.Machine,
        },
        new InputDeclaration { Name = "Encrypt", Label = "Encrypt the disk", Kind = InputKind.YesNo, Default = "true" },
        new InputDeclaration
        {
            Name = "JoinAccount",
            Label = "Join account",
            Kind = InputKind.Account,
            Required = true,
            Account = new AccountDestination { Domain = "corp.example" },
        },
        new InputDeclaration
        {
            Name = "Installer",
            Label = "Installer",
            Kind = InputKind.Account,
            Account = new AccountDestination { Hosts = ["files.corp.example"], RunAs = true },
        },
    ];

    // Every operator on a machine fact of a type it takes: numbers are compared as numbers, and only an address is in a
    // network.
    private static TestCondition Fitting(ConditionOperator op) => op switch
    {
        ConditionOperator.Greater or ConditionOperator.GreaterOrEqual or ConditionOperator.Less or ConditionOperator.LessOrEqual =>
            new TestCondition(MachineVariableNames.MemoryMegabytes, op, "8192"),
        ConditionOperator.InSubnet => new TestCondition(MachineVariableNames.IPv4Address, op, "10.0.0.0/24"),
        _ => new TestCondition(MachineVariableNames.Model, op, "Latitude 7440"),
    };

    // The kinds agents of this version run, by their type's name.
    private static string[] Kinds(int maximumVersion) =>
    [
        .. typeof(SequenceStep).GetCustomAttributes<JsonDerivedTypeAttribute>()
            .Where(kind => ((SequenceStep)RuntimeHelpers.GetUninitializedObject(kind.DerivedType)).MinimumVersion <= maximumVersion)
            .Select(kind => kind.DerivedType.Name)
            .Order(StringComparer.Ordinal),
    ];

    // A node's condition trees, every group and test in them.
    private static IEnumerable<ConditionNode> Conditions(SequenceStep node)
    {
        IEnumerable<ConditionNode?> roots = node switch
        {
            IfStep choice => [node.When, choice.Test],
            RepeatStep repeat => [node.When, repeat.Until],
            _ => [node.When],
        };

        return roots.OfType<ConditionNode>().SelectMany(Flatten);

        static IEnumerable<ConditionNode> Flatten(ConditionNode condition) =>
            condition is ConditionGroup group ? [condition, .. group.Parts.SelectMany(Flatten)] : [condition];
    }

    private static async Task MatchFixtureAsync<T>(string name, T value, string source)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string folder = Path.Combine(Repository.Root(), "src", "DDT.Web", "src", "test", "fixtures");
        string path = Path.Combine(folder, name);

        // Indented with LF line ends; the web's .prettierignore leaves the fixtures as written here.
        JsonSerializerOptions options = new(DdtJsonContext.Default.Options)
        {
            WriteIndented = true,
            NewLine = "\n",
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        string expected = JsonSerializer.Serialize(value, options) + "\n";
        string? actual = File.Exists(path) ? await File.ReadAllTextAsync(path, cancellationToken) : null;

        if (actual == expected)
        {
            return;
        }

        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(path, expected, cancellationToken);

        Assert.Fail($"{path} did not match {source} and was written again. Check the mirror in src/DDT.Web/src/sequences/sequences.ts against it, then commit it.");
    }

    private static Guid StepId(int index) =>
        new(string.Create(CultureInfo.InvariantCulture, $"00000000-0000-4000-8000-{index + 1:D12}"));
}
