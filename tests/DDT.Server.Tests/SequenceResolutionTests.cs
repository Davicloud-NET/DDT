// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Security.Cryptography;
using DDT.Contracts.Agents;
using DDT.Contracts.Machines;
using DDT.Contracts.Rules;
using DDT.Contracts.Sequences;
using DDT.Contracts.Values;
using DDT.Server.Authentication;
using DDT.Server.Rules;
using Xunit;

namespace DDT.Server.Tests;

public sealed class SequenceResolutionTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    private const string OnlyChooses =
        "A rule only chooses: the machine still needs an approval on the web, or someone who signs in at it, where the sequence is offered.";

    [Fact]
    public async Task TheFirstRuleThatMatchesChoosesUntilAnotherMovesAboveIt()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        SequenceView byModel = await application.RunnableSequenceAsync();
        SequenceView byMac = await application.RunnableSequenceAsync();
        string model = RuleRequests.UniqueModel();
        string mac = RuleRequests.RandomMac();
        using RegisteredMachine machine = await application.RegisterModelAsync("Dell Inc.", model, macs: mac);

        RuleView modelRule = await administrator.CreatedRuleAsync(RuleRequests.ModelRule(byModel.Id, model));
        MachineSequenceResolution chosenByModel = await administrator.ResolutionAsync(machine.Id);

        Assert.Equal(SequenceResolutionSource.Rule, chosenByModel.Source);
        Assert.Equal(byModel.Id, chosenByModel.SequenceId);
        Assert.Equal(byModel.Name, chosenByModel.SequenceName);
        Assert.Equal(modelRule.Id, chosenByModel.RuleId);
        Assert.Equal(0, chosenByModel.ProblemCount);
        Assert.Equal($"Rule {modelRule.Position + 1}, Model {model}, chooses {byModel.Name}. {OnlyChooses}", chosenByModel.Explanation);
        Assert.Equal("resolution.ruleNumbered", chosenByModel.ExplanationCode);
        Assert.Equal([modelRule.Id], chosenByModel.MatchedRuleIds);

        // Below the model rule it matches too, and chooses nothing.
        RuleView macRule = await administrator.CreatedRuleAsync(RuleRequests.MacRule(byMac.Id, mac));
        MachineSequenceResolution below = await administrator.ResolutionAsync(machine.Id);

        Assert.Equal(modelRule.Id, below.RuleId);
        Assert.Equal([modelRule.Id, macRule.Id], below.MatchedRuleIds);

        IReadOnlyList<RuleView> moved = await administrator.MoveRuleAboveAsync(macRule.Id, modelRule.Id);
        MachineSequenceResolution above = await administrator.ResolutionAsync(machine.Id);

        Assert.Equal(moved.Single(r => r.Id == modelRule.Id).Position - 1, moved.Single(r => r.Id == macRule.Id).Position);
        Assert.Equal((byMac.Id, macRule.Id), (above.SequenceId, above.RuleId));
        Assert.Equal([macRule.Id, modelRule.Id], above.MatchedRuleIds);
    }

    // Where rules name two of a machine's addresses, the higher one wins, not the one for its primary address.
    [Fact]
    public async Task OfTwoRulesForAMachinesAddressesTheHigherOneChooses()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        SequenceView sequence = await application.RunnableSequenceAsync();
        string primary = RuleRequests.RandomMac();
        string other = RuleRequests.RandomMac();
        using RegisteredMachine machine = await application.RegisterModelAsync("Dell Inc.", RuleRequests.UniqueModel(), macs: [primary, other]);

        RuleView byOther = await administrator.CreatedRuleAsync(RuleRequests.MacRule(sequence.Id, other));
        Assert.Equal(byOther.Id, (await administrator.ResolutionAsync(machine.Id)).RuleId);

        // Written as a person writes an address, which the test compares by its digits.
        string written = $"{primary[..2]}:{primary[2..4]}-{primary[4..].ToLowerInvariant()}";
        RuleView byPrimary = await administrator.CreatedRuleAsync(RuleRequests.MacRule(sequence.Id, written));
        MachineSequenceResolution resolution = await administrator.ResolutionAsync(machine.Id);

        Assert.Empty(byPrimary.Problems);
        Assert.Equal(byOther.Id, resolution.RuleId);
        Assert.Equal([byOther.Id, byPrimary.Id], resolution.MatchedRuleIds);
    }

    // A rule for a model's prefix matches the models that start with it, and rules for other makers and models never do.
    [Fact]
    public async Task AModelRuleMatchesAsTheAssignmentRuleItCameFromDid()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        Guid sequence = (await application.RunnableSequenceAsync()).Id;
        string family = $"Lat{Guid.NewGuid():N}";
        using RegisteredMachine machine = await application.RegisterModelAsync("Dell Inc.", $"{family} 5440");

        RuleView otherMaker = await administrator.CreatedRuleAsync(RuleRequests.ModelRule(sequence, $"{family} 5440", "Lenovo"));
        RuleView otherModel = await administrator.CreatedRuleAsync(RuleRequests.ModelRule(sequence, $"{family} 5440 2-in-1"));
        RuleView prefix = await administrator.CreatedRuleAsync(RuleRequests.ModelRule(sequence, $"{family} 54*", "Dell Inc."));
        RuleView exact = await administrator.CreatedRuleAsync(RuleRequests.ModelRule(sequence, $"{family} 5440"));

        MachineSequenceResolution resolution = await administrator.ResolutionAsync(machine.Id);

        Assert.Equal(prefix.Id, resolution.RuleId);
        Assert.Equal([prefix.Id, exact.Id], resolution.MatchedRuleIds);
        Assert.DoesNotContain(otherMaker.Id, resolution.MatchedRuleIds!);
        Assert.DoesNotContain(otherModel.Id, resolution.MatchedRuleIds!);
    }

    [Fact]
    public async Task MatchesAModelWhateverItsCaseAndSpacing()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        Guid sequence = (await application.RunnableSequenceAsync()).Id;
        string model = RuleRequests.UniqueModel();
        using RegisteredMachine machine = await application.RegisterModelAsync("  dell   inc. ", $"  {model.ToLowerInvariant().Replace(" ", "   ", StringComparison.Ordinal)} ");

        RuleView rule = await administrator.CreatedRuleAsync(RuleRequests.ModelRule(sequence, model.ToUpperInvariant(), "DELL INC."));

        Assert.Equal(rule.Id, (await administrator.ResolutionAsync(machine.Id)).RuleId);
    }

    // Firmware of a board nobody filled in says nothing about the machine, so no prefix may match its placeholder.
    [Fact]
    public async Task AMachineThatReportsAPlaceholderMatchesNoModelRule()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        Guid sequence = (await application.RunnableSequenceAsync()).Id;
        await administrator.CreatedRuleAsync(RuleRequests.ModelRule(sequence, "To Be*"));
        using RegisteredMachine machine = await application.RegisterModelAsync("To Be Filled By O.E.M.", "To Be Filled By O.E.M.");

        MachineSequenceResolution resolution = await administrator.ResolutionAsync(machine.Id);

        Assert.Equal(new MachineSequenceResolution(
            SequenceResolutionSource.None,
            null,
            null,
            null,
            0,
            "No rule chooses a sequence for this machine, so an operator chooses its sequence."), resolution);
        Assert.Empty(resolution.MatchedRuleIds!);
    }

    // A Gigabyte board reports its system's version and SKU and its asset tag as "Default string", which is no value.
    [Fact]
    public async Task AFactABoardMakerLeftAsAPlaceholderMatchesNothing()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        Guid sequence = (await application.RunnableSequenceAsync()).Id;
        string family = $"Z790 {Guid.NewGuid():N}";
        using RegisteredMachine machine = await application.RegisterWithAsync(registration => registration with
        {
            Manufacturer = "Gigabyte Technology Co., Ltd.",
            Model = "Z790 AORUS ELITE AX",
            Facts = new MachineFacts
            {
                SystemVersion = "Default string",
                SystemSku = "Default string",
                AssetTag = "Default string",
                SystemFamily = family,
            },
        });

        RuleView placeholder = await administrator.CreatedRuleAsync(RuleRequests.Rule(
            "Default string",
            new AllCondition
            {
                Parts =
                [
                    new TestCondition(MachineVariableNames.SystemFamily, ConditionOperator.Equals, family),
                    new AnyCondition
                    {
                        Parts =
                        [
                            new TestCondition(MachineVariableNames.SystemSku, ConditionOperator.Equals, "Default string"),
                            new TestCondition(MachineVariableNames.AssetTag, ConditionOperator.Exists),
                        ],
                    },
                ],
            },
            sequence));
        RuleView unset = await administrator.CreatedRuleAsync(RuleRequests.Rule(
            "No version",
            new AllCondition
            {
                Parts =
                [
                    new TestCondition(MachineVariableNames.SystemFamily, ConditionOperator.Equals, family),
                    new TestCondition(MachineVariableNames.SystemVersion, ConditionOperator.NotExists),
                ],
            },
            sequence));

        MachineSequenceResolution resolution = await administrator.ResolutionAsync(machine.Id);

        Assert.Empty(placeholder.Problems);
        Assert.Equal(unset.Id, resolution.RuleId);
        Assert.Equal([unset.Id], resolution.MatchedRuleIds);
    }

    [Fact]
    public async Task AnAssignmentOnTheWebComesBeforeEveryRule()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        string model = RuleRequests.UniqueModel();
        RuleView rule = await administrator.CreatedRuleAsync(RuleRequests.ModelRule((await application.RunnableSequenceAsync()).Id, model));
        using RegisteredMachine machine = await application.RegisterModelAsync("Dell Inc.", model);
        SequenceView assigned = await administrator.CreatedSequenceAsync(SequenceRequests.ScriptOnly());

        (await administrator.AssignAsync(machine.Id, assigned.Id)).EnsureSuccessStatusCode();
        MachineSequenceResolution resolution = await administrator.ResolutionAsync(machine.Id);

        Assert.Equal(SequenceResolutionSource.Assigned, resolution.Source);
        Assert.Null(resolution.RuleId);
        Assert.StartsWith("administrator-", resolution.Explanation, StringComparison.Ordinal);
        Assert.EndsWith($" assigned {assigned.Name} on the web, which comes before every rule.", resolution.Explanation, StringComparison.Ordinal);

        // The rule still matches: it sets values and gives machine roles for the run that was assigned.
        Assert.Equal([rule.Id], resolution.MatchedRuleIds);
    }

    [Fact]
    public async Task AChoiceAtTheMachineComesBeforeEveryRule()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        string operatorName = await application.CreateUserAsync(DdtRoleNames.Operator);
        await administrator.CreatedRuleAsync(RuleRequests.ModelRule((await application.RunnableSequenceAsync()).Id, "Virtual Machine", "Microsoft Corporation"));
        using DeployingMachine machine = await DeployingMachine.SignedInAsync(application, operatorName, [DeployingMachine.Disk(0)]);
        SequenceView chosen = await administrator.CreatedSequenceAsync(SequenceRequests.ScriptOnly());

        (await machine.Agent.PickRunAsync(machine.Id, machine.Token, new AgentRunRequest(chosen.Id, null, null))).EnsureSuccessStatusCode();
        MachineSequenceResolution resolution = await administrator.ResolutionAsync(machine.Id);

        Assert.Equal(SequenceResolutionSource.Console, resolution.Source);
        Assert.Equal($"{operatorName} chose {chosen.Name} at the machine, which comes before every rule.", resolution.Explanation);
    }

    [Fact]
    public async Task SaysWhenTheChosenSequenceCannotRun()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        SequenceView broken = await administrator.CreatedSequenceAsync(SequenceRequests.Minimal(Guid.Empty));
        string model = RuleRequests.UniqueModel();
        await administrator.CreatedRuleAsync(RuleRequests.ModelRule(broken.Id, model));
        using RegisteredMachine machine = await application.RegisterModelAsync(null, model);

        MachineSequenceResolution resolution = await administrator.ResolutionAsync(machine.Id);

        Assert.Equal(1, resolution.ProblemCount);
        Assert.EndsWith($" {broken.Name} has 1 problem, so it cannot run until it is fixed.", resolution.Explanation, StringComparison.Ordinal);
    }

    // A rule may test a value a rule above it sets, as one rule says where a machine is and others what goes there.
    [Fact]
    public async Task ARuleTestsAValueARuleAboveItSets()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        SequenceView vienna = await application.RunnableSequenceAsync();
        SequenceView graz = await application.RunnableSequenceAsync();
        string model = RuleRequests.UniqueModel();
        string site = $"Site{Guid.NewGuid():N}";
        using RegisteredMachine machine = await application.RegisterModelAsync("Dell Inc.", model);

        RuleView where = await administrator.CreatedRuleAsync(RuleRequests.ModelRule(null, model) with { Values = [new NamedValue(site, "Vienna")] });
        RuleView inGraz = await administrator.CreatedRuleAsync(RuleRequests.Rule("Graz", new TestCondition(site, ConditionOperator.Equals, "Graz"), graz.Id));
        RuleView inVienna = await administrator.CreatedRuleAsync(RuleRequests.Rule("Vienna", new TestCondition(site, ConditionOperator.Equals, "vienna"), vienna.Id));

        MachineSequenceResolution resolution = await administrator.ResolutionAsync(machine.Id);

        Assert.All([where, inGraz, inVienna], rule => Assert.Empty(rule.Problems));
        Assert.Equal((vienna.Id, inVienna.Id), (resolution.SequenceId, resolution.RuleId));
        Assert.Equal([where.Id, inVienna.Id], resolution.MatchedRuleIds);
        Assert.Equal(1, (await administrator.RulesAsync()).Single(r => r.Id == inVienna.Id).MatchingMachines);

        // Above the rule that sets it, the value is not set yet.
        await administrator.MoveRuleAboveAsync(inVienna.Id, where.Id);
        Assert.Equal([where.Id], (await administrator.ResolutionAsync(machine.Id)).MatchedRuleIds);
    }

    [Fact]
    public async Task ARuleWithProblemsOrTurnedOffNeverMatches()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        Guid sequence = (await application.RunnableSequenceAsync()).Id;
        string model = RuleRequests.UniqueModel();
        using RegisteredMachine machine = await application.RegisterModelAsync("Dell Inc.", model);

        RuleView unknown = await administrator.CreatedRuleAsync(RuleRequests.Rule(
            "Unknown",
            new AllCondition
            {
                Parts =
                [
                    new TestCondition(MachineVariableNames.Model, ConditionOperator.Equals, model),
                    new TestCondition($"Nothing{Guid.NewGuid():N}", ConditionOperator.NotExists),
                ],
            },
            sequence));
        RuleView off = await administrator.CreatedRuleAsync(RuleRequests.ModelRule(sequence, model) with { Enabled = false });

        MachineSequenceResolution resolution = await administrator.ResolutionAsync(machine.Id);

        Assert.NotEmpty(unknown.Problems);
        Assert.Equal(SequenceResolutionSource.None, resolution.Source);
        Assert.Empty(resolution.MatchedRuleIds!);

        // A rule turned off still says how many machines it would match; one with problems matches none.
        IReadOnlyList<RuleView> rules = await administrator.RulesAsync();
        Assert.Equal(1, rules.Single(r => r.Id == off.Id).MatchingMachines);
        Assert.Equal(0, rules.Single(r => r.Id == unknown.Id).MatchingMachines);
    }

    // What a run would start with: the machine's own name first, then the rules from the top, their machine roles, the
    // sequence's defaults and the deployment defaults, each with where it came from.
    [Fact]
    public async Task PreviewsTheValuesARunWouldStartWith()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        string model = RuleRequests.UniqueModel();
        string serial = $"SN-{Guid.NewGuid():N}";
        string computerName = $"PC-{serial.Replace("-", "", StringComparison.Ordinal)[^6..]}";
        MachineRoleView kiosk = await administrator.CreatedRoleAsync(new SaveMachineRoleRequest(
            0,
            $"Kiosk {Guid.NewGuid():N}",
            null,
            [new NamedValue("Office", "Graz"), new NamedValue("Wallpaper", "kiosk.png")]));
        Guid imageId = (await application.SeedImageAsync(RandomNumberGenerator.GetBytes(4096))).Id;
        SequenceView sequence = await administrator.CreatedSequenceAsync(SequenceRequests.Minimal(imageId) with
        {
            Variables =
            [
                new VariableDeclaration { Name = "Wallpaper", Default = "default.png" },
                new VariableDeclaration { Name = "Tag", Default = "{{Office|upper}}" },
            ],
            Inputs = [new InputDeclaration { Name = "Office", Label = "Office", Default = "Linz" }, new InputDeclaration { Name = "Owner", Label = "Owner" }],
        });
        RuleView named = await administrator.CreatedRuleAsync(RuleRequests.ModelRule(sequence.Id, model) with
        {
            Values = [new NamedValue("ComputerName", "PC-{{SerialNumber|alnum|right:6}}"), new NamedValue("Office", "Vienna")],
            RoleIds = [kiosk.Id],
        });
        using RegisteredMachine machine = await application.RegisterWithAsync(registration => registration with
        {
            Manufacturer = "Dell Inc.",
            Model = model,
            SerialNumber = serial,
        });

        MachineSequenceResolution resolution = await administrator.ResolutionAsync(machine.Id);
        Dictionary<string, ResolvedValue> used = resolution.Values!.Where(v => !v.Overridden).ToDictionary(v => v.Name);

        Assert.Equal([named.Id], resolution.MatchedRuleIds);
        Assert.Equal(new ResolvedValue("ComputerName", computerName, ValueSource.Rule, named.Id, named.Name, false), used["ComputerName"]);
        Assert.Equal(new ResolvedValue("Office", "Vienna", ValueSource.Rule, named.Id, named.Name, false), used["Office"]);
        Assert.Equal(new ResolvedValue("Wallpaper", "kiosk.png", ValueSource.Role, kiosk.Id, kiosk.Name, false), used["Wallpaper"]);
        Assert.Equal(new ResolvedValue("Tag", "VIENNA", ValueSource.SequenceDefault, null, null, false), used["Tag"]);
        Assert.Equal(
            new ResolvedValue(MachineValues.AdministratorName, "Admin", ValueSource.DeploymentDefault, null, null, false),
            used[MachineValues.AdministratorName]);
        Assert.Contains(new ResolvedValue("Office", "Graz", ValueSource.Role, kiosk.Id, kiosk.Name, true), resolution.Values!);
        Assert.Contains(new ResolvedValue("Office", "Linz", ValueSource.SequenceDefault, null, null, true), resolution.Values!);

        Assert.Equal(["Office", "Owner"], resolution.Inputs!.Select(input => input.Name));
        Assert.Equal([new ResolvedValue("Office", "Vienna", ValueSource.Rule, named.Id, named.Name, false)], resolution.InputDefaults);
        Assert.Empty(resolution.ValueProblems!);

        // A name the machine was given comes before every rule's, and the rules still give values to a run assigned on the
        // web.
        SequenceView script = await administrator.CreatedSequenceAsync(SequenceRequests.ScriptOnly());
        (await administrator.AssignAsync(machine.Id, script.Id, "PC-GIVEN")).EnsureSuccessStatusCode();
        MachineSequenceResolution assigned = await administrator.ResolutionAsync(machine.Id);

        Assert.Equal(
            [
                new ResolvedValue("ComputerName", "PC-GIVEN", ValueSource.Machine, null, null, false),
                new ResolvedValue("ComputerName", computerName, ValueSource.Rule, named.Id, named.Name, true),
            ],
            assigned.Values!.Where(v => v.Name == "ComputerName"));
        Assert.Contains(new ResolvedValue("Wallpaper", "kiosk.png", ValueSource.Role, kiosk.Id, kiosk.Name, false), assigned.Values!);
        Assert.Empty(assigned.Inputs!);
    }

    [Fact]
    public async Task SaysWhatKeepsTheValuesFromBeingWorkedOut()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        string model = RuleRequests.UniqueModel();
        await administrator.CreatedRuleAsync(RuleRequests.ModelRule(null, model) with
        {
            Values = [new NamedValue("ComputerName", "PC-{{AssetTag}}")],
        });
        using RegisteredMachine machine = await application.RegisterModelAsync("Dell Inc.", model);

        MachineSequenceResolution resolution = await administrator.ResolutionAsync(machine.Id);

        SequenceProblem problem = Assert.Single(resolution.ValueProblems!);
        Assert.Equal(("ComputerName", "values.cannotWorkOut"), (problem.Field, problem.Code));
        Assert.Equal(SequenceResolutionSource.None, resolution.Source);
        Assert.Empty(resolution.Inputs!);
    }

    [Fact]
    public async Task EveryoneSignedInReadsTheResolutionOfAMachineThatExists()
    {
        using SignedInClient viewer = await application.SignInAsync(DdtRoleNames.Viewer);
        using RegisteredMachine machine = await application.RegisterModelAsync("Dell Inc.", RuleRequests.UniqueModel());

        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"/api/machines/{machine.Id}/sequence")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync($"/api/machines/{Guid.NewGuid()}/sequence")).StatusCode);
    }

    [Fact]
    public async Task ListsTheModelsMachinesReportForThePickers()
    {
        using SignedInClient viewer = await application.SignInAsync(DdtRoleNames.Viewer);
        string model = RuleRequests.UniqueModel();
        using RegisteredMachine first = await application.RegisterModelAsync("Dell Inc.", model);
        using RegisteredMachine second = await application.RegisterModelAsync(" DELL  INC. ", $" {model.ToUpperInvariant()} ");
        using RegisteredMachine unset = await application.RegisterModelAsync("System manufacturer", $"{model} 2");
        using RegisteredMachine placeholder = await application.RegisterModelAsync("To Be Filled By O.E.M.", "To Be Filled By O.E.M.");

        IReadOnlyList<HardwareModelCount> models = await RegisteredMachine.ReadAsync<IReadOnlyList<HardwareModelCount>>(
            await viewer.GetAsync("/api/machines/models"));

        // Spelled as the first of its spellings in ordinal order.
        HardwareModelCount both = Assert.Single(models, m => string.Equals(m.Model, model, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(new HardwareModelCount("DELL INC.", model.ToUpperInvariant(), 2), both);
        Assert.Equal(new HardwareModelCount(null, $"{model} 2", 1), Assert.Single(models, m => m.Model == $"{model} 2"));
        Assert.DoesNotContain(models, m => m.Model.StartsWith("To Be", StringComparison.Ordinal));
    }
}
