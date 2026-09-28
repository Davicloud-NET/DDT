// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;
using DDT.Core.Sequences;
using Xunit;
using static DDT.Core.Tests.Sequences.TreeSteps;

namespace DDT.Core.Tests.Sequences;

// A sequence's variables and inputs, the templates that use them, the accounts steps name and the shares they connect.
public sealed class SequenceValidatorValueTests
{
    private static readonly Guid s_accountId = Guid.NewGuid();

    private static VariableDeclaration Variable(string name, bool setBySteps = false, string? value = null) =>
        new() { Name = name, Default = value, SetBySteps = setBySteps };

    private static InputDeclaration Input(string name, InputKind kind = InputKind.Text) => new() { Name = name, Label = name, Kind = kind };

    private static InputChoice[] Choices(params string[] values) => [.. values.Select(value => new InputChoice(value))];

    // A sequence that applies an image, then these steps, with these declarations: by default Office may be set by
    // steps, Owner is asked, and Installer is an account asked for the run.
    private static SequenceDefinition Declaring(
        IReadOnlyList<VariableDeclaration>? variables = null,
        IReadOnlyList<InputDeclaration>? inputs = null,
        params SequenceStep[] steps) =>
        Definition([Partition(), ApplyImage(), .. steps]) with
        {
            Variables = variables ?? [Variable("Office", setBySteps: true, "Standard"), Variable("Site")],
            Inputs = inputs ?? [Input("Owner"), Input("Installer", InputKind.Account)],
        };

    private static void AssertOnlyDeclaration(SequenceDefinition definition, string field, string code) =>
        AssertOnly(Validate(definition), null, field, code);

    [Fact]
    public void AcceptsWhatTheFlowBuilderWrites()
    {
        ShareConnection share = new(@"\\files.corp.example\drivers\{{Office}}", new AccountReference(s_accountId, null));
        SequenceDefinition definition = Declaring(
            [
                Variable("ComputerName", value: "PC-{{SerialNumber|alnum|right:12}}"),
                Variable("Office", setBySteps: true, "Standard"),
                Variable("Site"),
            ],
            [
                Input("Owner") with { MaxLength = 64, AskAt = InputAsk.Web },
                Input("Office", InputKind.Choice) with { Choices = Choices("Standard", "ProPlus"), Default = "standard" },
                Input("Languages", InputKind.MultiChoice) with { Choices = Choices("de-DE", "en-US"), Default = "de-DE; en-US" },
                Input("Encrypt", InputKind.YesNo) with { Default = "true", AskAt = InputAsk.Machine },
                Input("Installer", InputKind.Account),
            ],
            SetVariable("Office", "{{Office|upper}}-{{LastExitCode}}"),
            Pause("Check {{ComputerName}} for {{Owner}} in {{Site}}.") with { ContinueAfterMinutes = SequenceValidator.MaxPauseMinutes },
            Script(SequencePhase.Windows) with { RunAs = new AccountReference(null, "Installer"), Shares = [share] },
            JoinDomain() with { Account = new AccountReference(s_accountId, null), OrganizationalUnit = "OU={{Site}},DC=corp,DC=example" });

        Assert.Empty(Validate(definition));
    }

    [Theory]
    [InlineData("")]
    [InlineData("1st")]
    [InlineData("Has space")]
    [InlineData("Line\n")]
    [InlineData("A1234567890123456789012345678901234567890123456789012345678901234")]
    public void RefusesAVariableNameTemplatesCannotWrite(string name) =>
        AssertOnlyDeclaration(Declaring([Variable(name)], []), "variables[0].name", "sequence.valueName");

    [Fact]
    public void RefusesDdtsNamesAndTheFactsButComputerName()
    {
        AssertOnlyDeclaration(Declaring([Variable("ddtThing")], []), "variables[0].name", "sequence.valueNameReserved");
        AssertOnlyDeclaration(Declaring([Variable("model")], []), "variables[0].name", "values.fact");
        AssertOnlyDeclaration(Declaring([], [Input("LastExitCode")]), "inputs[0].name", "values.fact");
        Assert.Empty(Validate(Declaring([Variable("computerName")], [Input("ComputerName")])));
    }

    // An input may set a declared variable, which is how one is asked; an Account input sets none.
    [Fact]
    public void RefusesANameDeclaredTwice()
    {
        AssertOnlyDeclaration(Declaring([Variable("Office"), Variable("office")], []), "variables[1].name", "sequence.valueNameRepeated");
        AssertOnlyDeclaration(Declaring([], [Input("Owner"), Input("OWNER")]), "inputs[1].name", "sequence.valueNameRepeated");
        AssertOnlyDeclaration(Declaring([Variable("Installer")], [Input("Installer", InputKind.Account)]), "inputs[0].name", "sequence.valueNameRepeated");
        Assert.Empty(Validate(Declaring([Variable("Owner")], [Input("Owner")])));
    }

    [Fact]
    public void RefusesAnEmptyDeclaration()
    {
        AssertOnlyDeclaration(Declaring([null!], []), "variables[0]", "sequence.declarationEmpty");
        AssertOnlyDeclaration(Declaring([], [null!]), "inputs[0]", "sequence.declarationEmpty");
    }

    [Fact]
    public void DeclaresAtMostSixtyFourVariablesAndSixteenInputs()
    {
        VariableDeclaration[] variables = [.. Enumerable.Range(0, SequenceValidator.MaxVariables + 1).Select(index => Variable($"V{index}"))];
        InputDeclaration[] inputs = [.. Enumerable.Range(0, SequenceValidator.MaxInputs + 1).Select(index => Input($"I{index}"))];

        AssertOnlyDeclaration(Declaring(variables, []), "variables", "sequence.tooManyVariables");
        AssertOnlyDeclaration(Declaring([], inputs), "inputs", "sequence.tooManyInputs");
        Assert.Empty(Validate(Declaring(variables[1..], inputs[1..])));
    }

    [Theory]
    [InlineData("label")]
    [InlineData("kind")]
    [InlineData("askAt")]
    [InlineData("maxLength")]
    public void RefusesAnInputThatCannotBeAsked(string field)
    {
        InputDeclaration input = Input("Owner");
        InputDeclaration[] wrong = field switch
        {
            "label" => [input with { Label = " " }, input with { Label = new string('l', SequenceValidator.MaxNameLength + 1) }],
            "kind" => [input with { Kind = (InputKind)9 }],
            "askAt" => [input with { AskAt = (InputAsk)9 }],
            _ => [input with { MaxLength = 0 }, input with { MaxLength = SequenceValidator.MaxAnswerLength + 1 }],
        };

        foreach (InputDeclaration refused in wrong)
        {
            AssertOnlyDeclaration(Declaring([], [refused]), $"inputs[0].{field}", $"sequence.input{char.ToUpperInvariant(field[0])}{field[1..]}");
        }
    }

    [Fact]
    public void AsksOneToFiftyChoicesEachOnce()
    {
        InputDeclaration choice = Input("Office", InputKind.Choice) with { Choices = Choices("Standard", "ProPlus") };
        InputDeclaration several = Input("Languages", InputKind.MultiChoice) with { Choices = Choices("de-DE", "en-US") };

        AssertOnlyDeclaration(Declaring([], [choice with { Choices = [] }]), "inputs[0].choices", "sequence.inputChoices");
        AssertOnlyDeclaration(
            Declaring([], [choice with { Choices = Choices([.. Enumerable.Range(0, SequenceValidator.MaxChoices + 1).Select(i => $"C{i}")]) }]),
            "inputs[0].choices",
            "sequence.inputChoices");
        AssertOnlyDeclaration(Declaring([], [choice with { Choices = Choices("Standard", " ") }]), "inputs[0].choices[1].value", "sequence.inputChoiceEmpty");
        AssertOnlyDeclaration(Declaring([], [choice with { Choices = Choices("Standard", "standard") }]), "inputs[0].choices[1].value", "sequence.inputChoiceRepeated");
        AssertOnlyDeclaration(Declaring([], [several with { Choices = Choices("de-DE;en-US") }]), "inputs[0].choices[0].value", "sequence.inputChoiceSemicolon");
        AssertOnlyDeclaration(Declaring([], [choice with { Default = "Home" }]), "inputs[0].default", "sequence.inputDefaultNotChoice");
        AssertOnlyDeclaration(Declaring([], [several with { Default = "de-DE;fr-FR" }]), "inputs[0].default", "sequence.inputDefaultNotChoice");

        // A semicolon is a value like any other when only one answer is chosen, and choices are for choosing.
        Assert.Empty(Validate(Declaring([], [choice with { Choices = Choices("a;b") }, Input("Owner") with { Choices = [] }])));
    }

    [Fact]
    public void ChecksTheTemplatesOfEveryStepAndDefault()
    {
        // The deployment defaults are values of every run.
        WriteUnattendStep unattend = WriteUnattend() with { TimeZone = "{{TimeZone}}", Locale = "{{Locale|lower}}", Keyboard = "{{Keyboard}}" };
        Assert.Empty(Validate(Declaring(null, null, unattend)));

        SetVariableStep set = SetVariable("Office", "{{Offce}}");
        PauseStep pause = Pause("{{ComputerName|right}}");
        JoinDomainStep join = JoinDomain() with { OrganizationalUnit = "OU={{Site|shout}}" };
        WriteUnattendStep zone = WriteUnattend() with { TimeZone = "{{Zone}}" };

        Assert.Equal(
            [
                (null, "variables[1].default", "valueTemplate.unknownName"),
                (set.Id, "value", "valueTemplate.unknownName"),
                (pause.Id, "message", "valueTemplate.filterNeedsCount"),
                (zone.Id, "timeZone", "valueTemplate.unknownName"),
                (join.Id, "organizationalUnit", "valueTemplate.unknownFilter"),
            ],
            Said(Validate(Declaring([Variable("Office", setBySteps: true), Variable("Site", value: "{{Nowhere}}")], null, set, pause, zone, join))));
    }

    [Fact]
    public void NeverPutsAnAccountInputIntoText()
    {
        PauseStep pause = Pause("Signed in as {{Installer}}.");

        AssertOnly(Validate(Declaring(null, null, pause)), pause, "message", "sequence.accountInputAsValue");
    }

    [Fact]
    public void SetsOnlyAVariableStepsMaySet()
    {
        SetVariableStep none = SetVariable(" ", "x");
        SetVariableStep undeclared = SetVariable("Region", "x");
        SetVariableStep fixedOne = SetVariable("site", "x");

        AssertOnly(Validate(Declaring(null, null, none)), none, "variable", "sequence.setVariableChoose");
        AssertOnly(Validate(Declaring(null, null, undeclared)), undeclared, "variable", "sequence.variableNotDeclared");
        AssertOnly(Validate(Declaring(null, null, fixedOne)), fixedOne, "variable", "sequence.variableNotSetBySteps");
        Assert.Empty(Validate(Declaring(null, null, SetVariable("office", "{{Owner}}"))));
    }

    [Fact]
    public void RunsAsAnAccountOnlyInWindows()
    {
        RunScriptStep inWindowsPE = Script(SequencePhase.WindowsPE) with { RunAs = new AccountReference(s_accountId, null) };

        AssertOnly(Validate(Definition(Partition(), inWindowsPE)), inWindowsPE, "runAs", "sequence.runAsWindowsOnly");
    }

    [Fact]
    public void NamesExactlyOneAccountAndOnlyAnAccountInput()
    {
        foreach (AccountReference wrong in new AccountReference[] { new(null, null), new(Guid.Empty, " "), new(s_accountId, "Installer") })
        {
            RunScriptStep script = Script(SequencePhase.Windows) with { RunAs = wrong };

            AssertOnly(Validate(Declaring(null, null, script)), script, "runAs", "sequence.accountChoose");
        }

        // The run keeps the answer under the input's name as it is written.
        foreach (string input in new[] { "Owner", "installer", "Nobody" })
        {
            JoinDomainStep join = JoinDomain() with { Account = new AccountReference(null, input) };

            AssertOnly(Validate(Declaring(null, null, join)), join, "account.input", "sequence.accountInputUnknown");
        }
    }

    [Fact]
    public void ConnectsAtMostFourSharesWrittenAsHostAndShare()
    {
        AccountReference account = new(null, "Installer");
        ShareConnection good = new(@"\\files.corp.example\drivers", account);

        foreach (string path in new[] { "", "files.corp.example\\drivers", @"\\files.corp.example", @"\\files.corp.example\", @"\\\drivers", "//files/drivers" })
        {
            RebootStep step = Reboot() with { Shares = [good, good with { Path = path }] };

            AssertOnly(Validate(Declaring(null, null, step)), step, "shares[1].path", "sequence.sharePath");
        }

        RebootStep tooMany = Reboot() with { Shares = [.. Enumerable.Repeat(good, SequenceValidator.MaxShares + 1)] };
        RebootStep noAccount = Reboot() with { Shares = [good with { Account = null! }] };

        AssertOnly(Validate(Declaring(null, null, tooMany)), tooMany, "shares", "sequence.tooManyShares");
        AssertOnly(Validate(Declaring(null, null, noAccount)), noAccount, "shares[0].account", "sequence.accountChoose");
        Assert.Empty(Validate(Declaring(null, null, Reboot() with { Shares = [.. Enumerable.Repeat(good, SequenceValidator.MaxShares)] })));
    }

    // A share is connected while its step runs, and a container runs nothing itself.
    [Fact]
    public void ConnectsSharesOnlyForAStep()
    {
        ShareConnection share = new(@"\\files.corp.example\drivers", new AccountReference(s_accountId, null));

        foreach (SequenceStep container in new SequenceStep[] { Group(Reboot()), If([Reboot()]), Repeat(Reboot()) })
        {
            SequenceStep holding = container with { Shares = [share] };

            AssertOnly(Validate(Declaring(null, null, holding)), holding, "shares", "sequence.sharesOnlyOnSteps");
        }
    }

    // A step could otherwise send the account to a host of its choosing, by a variable it sets or a script's exit code.
    [Fact]
    public void TakesAShareHostOnlyFromValuesFixedWhenTheRunStarts()
    {
        AccountReference account = new(s_accountId, null);

        foreach (string host in new[] { "{{Office}}", "files-{{LastExitCode}}.corp.example", "{{PHASE}}" })
        {
            RebootStep step = Reboot() with { Shares = [new ShareConnection($@"\\{host}\drivers", account)] };

            IReadOnlyList<SequenceProblem> problems = Validate(Declaring(null, null, step));

            AssertOnly(problems, step, "shares[0].path", "sequence.shareHostFixed");
        }

        RebootStep fixedHost = Reboot() with
        {
            Shares = [new ShareConnection(@"\\{{Site}}-{{Owner}}.{{DnsSuffix}}\drivers\{{Office}}\{{LastExitCode}}", account)],
        };
        Assert.Empty(Validate(Declaring(null, null, fixedHost)));
    }

    [Fact]
    public void GoesOnAfterAPauseOfOneToADayOfMinutes()
    {
        foreach (int minutes in new[] { 0, SequenceValidator.MaxPauseMinutes + 1 })
        {
            PauseStep pause = Pause() with { ContinueAfterMinutes = minutes };

            AssertOnly(Validate(Partition(), pause), pause, "continueAfterMinutes", "sequence.pauseMinutes");
        }

        Assert.Empty(Validate(Partition(), Pause() with { ContinueAfterMinutes = 1 }, Pause() with { ContinueAfterMinutes = null }));
    }
}
