// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;
using DDT.Contracts.Sequences;
using DDT.Contracts.Values;
using DDT.Core.Sequences;
using DDT.Core.Values;
using Xunit;

namespace DDT.Core.Tests.Values;

public sealed class ValueResolverTests
{
    private static readonly Guid s_firstRule = new("0193a4b2-0000-7000-8000-0000000000d1");
    private static readonly Guid s_secondRule = new("0193a4b2-0000-7000-8000-0000000000d2");
    private static readonly Guid s_kiosk = new("0193a4b2-0000-7000-8000-0000000000e1");
    private static readonly Guid s_finance = new("0193a4b2-0000-7000-8000-0000000000e2");

    private static readonly MachineVariables s_facts = new(
        "Dell Inc.",
        "Latitude 5440",
        "cn-0k2p1x-12345-abc",
        "4c4c4544-0042-3510-8052-b4c04f4d3232",
        ["00155D010203"],
        null,
        SequencePhase.WindowsPE);

    private static readonly SequenceDefinition s_sequence = new(SequenceDefinition.CurrentVersion, [])
    {
        Variables =
        [
            new VariableDeclaration { Name = "Office", Default = "Standard" },
            new VariableDeclaration { Name = MachineVariableNames.ComputerName, Default = "PC-{{SerialNumber|alnum|right:8|upper}}" },
            new VariableDeclaration { Name = "Unset" },
        ],
        Inputs =
        [
            new InputDeclaration { Name = "Office", Label = "Office edition", Default = "Business" },
            new InputDeclaration { Name = "Owner", Label = "Owner", Required = true },
            new InputDeclaration { Name = "JoinAccount", Label = "Join account", Kind = InputKind.Account, Required = true },
        ],
    };

    // A required input without a default of its own.
    private static readonly SequenceDefinition s_owner = new(SequenceDefinition.CurrentVersion, [])
    {
        Inputs =
        [
            new InputDeclaration { Name = "Owner", Label = "Owner", Required = true },
            new InputDeclaration { Name = "JoinAccount", Label = "Join account", Kind = InputKind.Account, Required = true },
        ],
    };

    // Every source sets Office: the answer wins, and the rest are shown overridden in the order they would win.
    [Fact]
    public void TakesEachNameFromTheFirstSourceThatSetsIt()
    {
        ValueResolution resolution = ValueResolver.Resolve(AllSources());

        Assert.Equal(
            [
                new ResolvedValue("Office", "Answered", ValueSource.Input, null, null, false),
                new ResolvedValue("Office", "Machine's", ValueSource.Machine, null, null, true),
                new ResolvedValue("Office", "First rule's", ValueSource.Rule, s_firstRule, "Dell laptops", true),
                new ResolvedValue("Office", "Second rule's", ValueSource.Rule, s_secondRule, "Everything", true),
                new ResolvedValue("Office", "Kiosk's", ValueSource.Role, s_kiosk, "Kiosk", true),
                new ResolvedValue("Office", "Finance's", ValueSource.Role, s_finance, "Finance", true),
                new ResolvedValue("Office", "Business", ValueSource.SequenceDefault, null, null, true),
                new ResolvedValue("Office", "Standard", ValueSource.SequenceDefault, null, null, true),
                new ResolvedValue("Office", "Deployment's", ValueSource.DeploymentDefault, null, null, true),
            ],
            resolution.Values.Where(value => value.Name == "Office"));
        Assert.Equal("Answered", resolution.Effective["office"]);
    }

    [Fact]
    public void FallsToTheNextSourceWhenOneSetsNothing()
    {
        ValueSources sources = AllSources();
        List<string> winners = [];

        foreach (Func<ValueSources, ValueSources> without in new Func<ValueSources, ValueSources>[]
        {
            current => current with { Answers = new Dictionary<string, string> { ["Owner"] = "Ann", ["Office"] = "" } },
            current => current with { Machine = [] },
            current => current with { Rules = [current.Rules[1]] },
            current => current with { Rules = [] },
            current => current with { Roles = [current.Roles[1]] },
            current => current with { Roles = [] },
            current => current with { Sequence = s_sequence with { Inputs = [.. s_sequence.Inputs!.Skip(1)] } },
            current => current with { Sequence = s_sequence with { Inputs = [], Variables = [] } },
            current => current with { DeploymentDefaults = [] },
        })
        {
            sources = without(sources);
            ValueResolution resolution = ValueResolver.Resolve(sources);
            winners.Add(resolution.Effective.GetValueOrDefault("Office") ?? "none");
            Assert.All(resolution.Values.Where(value => value.Name == "Office").Skip(1), value => Assert.True(value.Overridden));
        }

        Assert.Equal(
            ["Machine's", "First rule's", "Second rule's", "Kiosk's", "Finance's", "Business", "Standard", "Deployment's", "none"],
            winners);
    }

    [Fact]
    public void RendersTemplatesFromOtherValuesAndFacts()
    {
        ValueResolution resolution = ValueResolver.Resolve(new ValueSources
        {
            Sequence = s_sequence with { Inputs = null },
            Rules = [new ValueSet(s_firstRule, "Site", [new NamedValue("Site", "Wien"), new NamedValue("Ou", "OU={{Site}},OU={{Office|upper}}")])],
            Facts = s_facts,
        });

        Assert.Empty(resolution.Problems);
        Assert.Equal(["ComputerName=PC-12345ABC", "Office=Standard", "Ou=OU=Wien,OU=STANDARD", "Site=Wien"], Pairs(resolution.Effective));
        Assert.Equal(["Office", MachineVariableNames.ComputerName, "Site", "Ou"], resolution.Values.Select(value => value.Name));
        Assert.DoesNotContain(resolution.Values, value => value.Name == "Unset");
    }

    // A value the page shows as overridden is shown as it would have been, and as it is written where that cannot be
    // told.
    [Fact]
    public void ShowsOverriddenTemplatesRendered()
    {
        ValueResolution resolution = ValueResolver.Resolve(new ValueSources
        {
            Rules =
            [
                new ValueSet(s_firstRule, "First", [new NamedValue("Tag", "A-{{Model}}")]),
                new ValueSet(s_secondRule, "Second", [new NamedValue("Tag", "B-{{Model|upper}}"), new NamedValue("Tag", "C-{{Nothing}}")]),
            ],
            Facts = s_facts,
        });

        Assert.Equal(["A-Latitude 5440", "B-LATITUDE 5440", "C-{{Nothing}}"], resolution.Values.Select(value => value.Value));
        Assert.Equal([false, true, true], resolution.Values.Select(value => value.Overridden));
        Assert.Empty(resolution.Problems);
    }

    [Fact]
    public void FindsAValueMadeFromItself()
    {
        ValueResolution resolution = ValueResolver.Resolve(new ValueSources
        {
            Rules =
            [
                new ValueSet(s_firstRule, "Loop", [new NamedValue("A", "{{B}}-a"), new NamedValue("B", "{{c}}-b"), new NamedValue("C", "{{A}}")]),
                new ValueSet(s_secondRule, "After", [new NamedValue("D", "{{b}}"), new NamedValue("Fine", "fine")]),
            ],
        });

        ValueProblem problem = Assert.Single(resolution.Problems);
        Assert.Equal("A", problem.Name);
        Assert.Equal(ServerMessages.ValuesCycle.Code, problem.Message.Code);
        Assert.Equal("A cannot be worked out, because it is made from itself: A > B > C > A.", problem.Message.Text);
        Assert.Equal(["Fine=fine"], Pairs(resolution.Effective));
        Assert.Equal("{{B}}-a", resolution.Values[0].Value);
    }

    [Fact]
    public void FindsAComputerNameMadeFromItself()
    {
        ValueResolution resolution = ValueResolver.Resolve(new ValueSources
        {
            Rules = [new ValueSet(s_firstRule, "Rename", [new NamedValue(MachineVariableNames.ComputerName, "{{computername|upper}}")])],
            Facts = s_facts with { ComputerName = "pc-1" },
        });

        Assert.Equal(
            "ComputerName cannot be worked out, because it is made from itself: ComputerName > ComputerName.",
            Assert.Single(resolution.Problems).Message.Text);
        Assert.Empty(resolution.Effective);
    }

    // The problem is where the value is missing, and the values made from it fail without a problem of their own.
    [Fact]
    public void NamesAValueTheMachineHasNot()
    {
        ValueResolution resolution = ValueResolver.Resolve(new ValueSources
        {
            Rules = [new ValueSet(s_firstRule, "Rule", [new NamedValue("Asset", "{{AssetTag|upper}}"), new NamedValue("Label", "{{Asset}}")])],
            Facts = s_facts,
        });

        ValueProblem problem = Assert.Single(resolution.Problems);
        ServerMessage missing = ServerMessages.ValueTemplateNoValue.With("placeholder", "{{AssetTag|upper}}");

        Assert.Equal(new ValueProblem("Asset", ServerMessages.ValuesCannotWorkOut.With("name", "Asset", "problem", missing)), problem);
        Assert.Equal("Asset cannot be worked out. The machine has no value for {{AssetTag|upper}}.", problem.Message.Text);
        Assert.Empty(resolution.Effective);
    }

    [Fact]
    public void RefusesAComputerNameWindowsWouldRefuse()
    {
        ValueResolution resolution = ValueResolver.Resolve(new ValueSources
        {
            Rules = [new ValueSet(s_firstRule, "Rule", [new NamedValue(MachineVariableNames.ComputerName, "LAPTOP-{{SerialNumber}}")])],
            Facts = s_facts,
        });

        ValueProblem problem = Assert.Single(resolution.Problems);
        Assert.Equal(MachineVariableNames.ComputerName, problem.Name);
        Assert.Equal(ServerMessages.ValuesComputerName.Code, problem.Message.Code);
        Assert.Equal(
            "The computer name 'LAPTOP-cn-0k2p1x-12345-abc' cannot be used. A computer name can have at most 15 characters.",
            problem.Message.Text);
        Assert.Equal("LAPTOP-cn-0k2p1x-12345-abc", resolution.Effective[MachineVariableNames.ComputerName]);
    }

    [Fact]
    public void TakesTheMachinesAssignedNameAsTheComputerName()
    {
        ValueResolution resolution = ValueResolver.Resolve(new ValueSources
        {
            Sequence = s_sequence with { Inputs = null },
            Machine = [new NamedValue(MachineVariableNames.ComputerName, "PC-ASSIGNED")],
            Facts = s_facts,
        });

        Assert.Empty(resolution.Problems);
        Assert.Equal("PC-ASSIGNED", resolution.Effective[MachineVariableNames.ComputerName]);
        Assert.Equal(
            [
                new ResolvedValue(MachineVariableNames.ComputerName, "PC-ASSIGNED", ValueSource.Machine, null, null, false),
                new ResolvedValue(MachineVariableNames.ComputerName, "PC-12345ABC", ValueSource.SequenceDefault, null, null, true),
            ],
            resolution.Values.Where(value => value.Name == MachineVariableNames.ComputerName));
    }

    [Fact]
    public void AsksForARequiredInputWithoutAnAnswerOrADefault()
    {
        ValueResolution unanswered = ValueResolver.Resolve(new ValueSources { Sequence = s_sequence, Facts = s_facts });

        Assert.Equal([new ValueProblem("Owner", ServerMessages.ValuesInputRequired.With("label", "Owner"))], unanswered.Problems);
        Assert.Equal("Owner needs an answer before the run can start.", unanswered.Problems[0].Message.Text);

        ValueResolution blank = ValueResolver.Resolve(new ValueSources
        {
            Sequence = s_sequence,
            Answers = new Dictionary<string, string> { ["owner"] = "" },
            Facts = s_facts,
        });

        Assert.Single(blank.Problems);

        ValueResolution answered = ValueResolver.Resolve(new ValueSources
        {
            Sequence = s_sequence,
            Answers = new Dictionary<string, string> { ["owner"] = "Ann", ["JoinAccount"] = "corp\\joiner", ["Undeclared"] = "x" },
            Facts = s_facts,
        });

        Assert.Empty(answered.Problems);
        Assert.Equal("Ann", answered.Effective["Owner"]);
        Assert.False(answered.Effective.ContainsKey("JoinAccount"));
        Assert.False(answered.Effective.ContainsKey("Undeclared"));
        Assert.Equal(new ResolvedValue("Owner", "Ann", ValueSource.Input, null, null, false), Assert.Single(answered.Values, value => value.Name == "Owner"));

        SequenceDefinition withDefault = s_sequence with
        {
            Inputs = [new InputDeclaration { Name = "Owner", Label = "Owner", Required = true, Default = "IT" }],
        };

        Assert.Empty(ValueResolver.Resolve(new ValueSources { Sequence = withDefault, Facts = s_facts }).Problems);
    }

    // Facts belong to the machine and the run variables to the run.
    [Fact]
    public void RefusesAValueNamedAsAFact()
    {
        ValueResolution resolution = ValueResolver.Resolve(new ValueSources
        {
            Rules = [new ValueSet(s_firstRule, "Rule", [new NamedValue("model", "Precision"), new NamedValue(MachineVariableNames.LastExitCode, "0")])],
            Roles = [new ValueSet(s_kiosk, "Kiosk", [new NamedValue("Model", "Precision")])],
            Facts = s_facts,
        });

        Assert.Equal(
            [
                new ValueProblem("model", ServerMessages.ValuesFact.With("name", "model")),
                new ValueProblem(MachineVariableNames.LastExitCode, ServerMessages.ValuesFact.With("name", MachineVariableNames.LastExitCode)),
                new ValueProblem("Model", ServerMessages.ValuesFact.With("name", "Model")),
            ],
            resolution.Problems);
        Assert.Empty(resolution.Values);
    }

    // Names ignore case: the sequence's spelling wins, and one source's setting a name twice keeps the first.
    [Fact]
    public void TakesANameWhateverItsCase()
    {
        ValueResolution resolution = ValueResolver.Resolve(new ValueSources
        {
            Sequence = s_sequence with { Inputs = null },
            Rules = [new ValueSet(s_firstRule, "Rule", [new NamedValue("OFFICE", "ProPlus"), new NamedValue("office", "Home")])],
            Facts = s_facts,
        });

        Assert.Empty(resolution.Problems);

        Assert.Equal(["ProPlus", "Home", "Standard"], resolution.Values.Where(value => value.Name == "Office").Select(value => value.Value));
        Assert.DoesNotContain(resolution.Values, value => value.Name is "OFFICE" or "office");
        Assert.Equal("ProPlus", resolution.Effective["Office"]);
    }

    [Fact]
    public void TakesAnswersAndDeploymentDefaultsAsTheyAreWritten()
    {
        ValueResolution resolution = ValueResolver.Resolve(new ValueSources
        {
            Sequence = new SequenceDefinition(SequenceDefinition.CurrentVersion, [])
            {
                Inputs = [new InputDeclaration { Name = "Note", Label = "Note" }],
            },
            Answers = new Dictionary<string, string> { ["Note"] = "{{Model}}" },
            DeploymentDefaults = [new NamedValue("TimeZone", "{{W. Europe}}")],
            Facts = s_facts,
        });

        Assert.Empty(resolution.Problems);
        Assert.Equal("{{Model}}", resolution.Effective["Note"]);
        Assert.Equal("{{W. Europe}}", resolution.Effective["TimeZone"]);
    }

    [Fact]
    public void ResolvesNothingFromNothing()
    {
        ValueResolution resolution = ValueResolver.Resolve(new ValueSources());

        Assert.Empty(resolution.Values);
        Assert.Empty(resolution.Effective);
        Assert.Empty(resolution.Problems);
        Assert.Empty(resolution.InputDefaults);
    }

    // A rule's value is the input's default, so the question starts with it and a required input is answered by it.
    [Fact]
    public void AnswersARequiredInputWithARulesValue()
    {
        ValueResolution resolution = ValueResolver.Resolve(new ValueSources
        {
            Sequence = s_owner,
            Rules = [new ValueSet(s_firstRule, "Finance laptops", [new NamedValue("owner", "Finance {{Model|upper}}")])],
            Facts = s_facts,
        });

        Assert.Empty(resolution.Problems);
        Assert.Equal("Finance LATITUDE 5440", resolution.Effective["Owner"]);
        Assert.Equal(
            new ResolvedValue("Owner", "Finance LATITUDE 5440", ValueSource.Rule, s_firstRule, "Finance laptops", false),
            Assert.Single(resolution.Values));
        Assert.Equal(
            [new ResolvedValue("Owner", "Finance LATITUDE 5440", ValueSource.Rule, s_firstRule, "Finance laptops", false)],
            resolution.InputDefaults);
    }

    [Fact]
    public void AnswersARequiredInputWithAMachineRolesValue()
    {
        ValueResolution resolution = ValueResolver.Resolve(new ValueSources
        {
            Sequence = s_owner,
            Rules = [new ValueSet(s_firstRule, "Kiosks", [new NamedValue("Site", "Wien")])],
            Roles = [new ValueSet(s_kiosk, "Kiosk", [new NamedValue("Owner", "Front desk")])],
            Facts = s_facts,
        });

        Assert.Empty(resolution.Problems);
        Assert.Equal("Front desk", resolution.Effective["Owner"]);
        Assert.Equal([new ResolvedValue("Owner", "Front desk", ValueSource.Role, s_kiosk, "Kiosk", false)], resolution.InputDefaults);
    }

    // An answer still wins; the default it overrode stays the rule's, for the question.
    [Fact]
    public void LetsAnAnswerOverrideARulesValue()
    {
        ValueResolution resolution = ValueResolver.Resolve(new ValueSources
        {
            Sequence = s_owner,
            Answers = new Dictionary<string, string> { ["Owner"] = "Ann" },
            Rules = [new ValueSet(s_firstRule, "Finance laptops", [new NamedValue("Owner", "Finance")])],
            Facts = s_facts,
        });

        Assert.Empty(resolution.Problems);
        Assert.Equal("Ann", resolution.Effective["Owner"]);
        Assert.Equal(
            [
                new ResolvedValue("Owner", "Ann", ValueSource.Input, null, null, false),
                new ResolvedValue("Owner", "Finance", ValueSource.Rule, s_firstRule, "Finance laptops", true),
            ],
            resolution.Values);
        Assert.Equal([new ResolvedValue("Owner", "Finance", ValueSource.Rule, s_firstRule, "Finance laptops", true)], resolution.InputDefaults);

        ValueResolution blank = ValueResolver.Resolve(new ValueSources
        {
            Sequence = s_owner,
            Answers = new Dictionary<string, string> { ["Owner"] = "" },
            Rules = [new ValueSet(s_firstRule, "Finance laptops", [new NamedValue("Owner", "Finance")])],
            Facts = s_facts,
        });

        Assert.Equal("Finance", blank.Effective["Owner"]);
        Assert.Equal([new ResolvedValue("Owner", "Finance", ValueSource.Rule, s_firstRule, "Finance laptops", false)], blank.InputDefaults);
    }

    [Fact]
    public void UsesTheInputsOwnDefaultOnlyWhenNoRuleOrRoleSetsItsName()
    {
        SequenceDefinition withDefault = s_owner with
        {
            Inputs = [new InputDeclaration { Name = "Owner", Label = "Owner", Required = true, Default = "IT" }],
        };

        ValueResolution byRule = ValueResolver.Resolve(new ValueSources
        {
            Sequence = withDefault,
            Rules = [new ValueSet(s_firstRule, "Finance laptops", [new NamedValue("Owner", "Finance")])],
            Facts = s_facts,
        });
        ValueResolution byDefault = ValueResolver.Resolve(new ValueSources
        {
            Sequence = withDefault,
            Rules = [new ValueSet(s_firstRule, "Kiosks", [new NamedValue("Site", "Wien")])],
            Facts = s_facts,
        });

        Assert.Equal("Finance", byRule.Effective["Owner"]);
        Assert.Equal([new ResolvedValue("Owner", "Finance", ValueSource.Rule, s_firstRule, "Finance laptops", false)], byRule.InputDefaults);
        Assert.Empty(byDefault.Problems);
        Assert.Equal("IT", byDefault.Effective["Owner"]);
        Assert.Equal([new ResolvedValue("Owner", "IT", ValueSource.SequenceDefault, null, null, false)], byDefault.InputDefaults);
    }

    // The machine's own value comes before the rules', so it is the default the question starts with.
    [Fact]
    public void TakesTheMachinesOwnValueAsTheDefaultBeforeARules()
    {
        SequenceDefinition named = new(SequenceDefinition.CurrentVersion, [])
        {
            Inputs = [new InputDeclaration { Name = MachineVariableNames.ComputerName, Label = "Computer name", Required = true }],
        };

        ValueResolution resolution = ValueResolver.Resolve(new ValueSources
        {
            Sequence = named,
            Machine = [new NamedValue(MachineVariableNames.ComputerName, "PC-ASSIGNED")],
            Rules = [new ValueSet(s_firstRule, "Names", [new NamedValue(MachineVariableNames.ComputerName, "PC-{{SerialNumber|alnum|right:8}}")])],
            Facts = s_facts,
        });

        Assert.Empty(resolution.Problems);
        Assert.Equal(
            [new ResolvedValue(MachineVariableNames.ComputerName, "PC-ASSIGNED", ValueSource.Machine, null, null, false)],
            resolution.InputDefaults);
    }

    private static string[] Pairs(IReadOnlyDictionary<string, string> values) =>
        [.. values.Select(pair => $"{pair.Key}={pair.Value}").Order(StringComparer.Ordinal)];

    private static ValueSources AllSources() => new()
    {
        Sequence = s_sequence,
        Answers = new Dictionary<string, string> { ["Office"] = "Answered", ["Owner"] = "Ann" },
        Machine = [new NamedValue("Office", "Machine's")],
        Rules =
        [
            new ValueSet(s_firstRule, "Dell laptops", [new NamedValue("Office", "First rule's")]),
            new ValueSet(s_secondRule, "Everything", [new NamedValue("Office", "Second rule's")]),
        ],
        Roles =
        [
            new ValueSet(s_kiosk, "Kiosk", [new NamedValue("Office", "Kiosk's")]),
            new ValueSet(s_finance, "Finance", [new NamedValue("Office", "Finance's")]),
        ],
        DeploymentDefaults = [new NamedValue("Office", "Deployment's")],
        Facts = s_facts,
    };
}
