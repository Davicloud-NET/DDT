// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Security.Cryptography;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Contracts.Rules;
using DDT.Contracts.Sequences;
using DDT.Server.Authentication;
using DDT.Server.Images;
using Xunit;

namespace DDT.Server.Tests;

public sealed class SequenceResolutionTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    [Fact]
    public async Task AMacRuleComesBeforeAModelRule()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        SequenceView byModel = await application.RunnableSequenceAsync();
        SequenceView byMac = await application.RunnableSequenceAsync();
        string model = RuleRequests.UniqueModel();
        string mac = RuleRequests.RandomMac();
        using RegisteredMachine machine = await application.RegisterModelAsync("Dell Inc.", model, macs: mac);

        AssignmentRuleView modelRule = await administrator.CreatedRuleAsync(RuleRequests.ModelRule(byModel.Id, model));
        MachineSequenceResolution chosenByModel = await administrator.ResolutionAsync(machine.Id);

        Assert.Equal(SequenceResolutionSource.ModelRule, chosenByModel.Source);
        Assert.Equal(byModel.Id, chosenByModel.SequenceId);
        Assert.Equal(byModel.Name, chosenByModel.SequenceName);
        Assert.Equal(modelRule.Id, chosenByModel.RuleId);
        Assert.Equal(0, chosenByModel.ProblemCount);
        Assert.Equal(
            $"The rule for model {model} of any maker chooses {byModel.Name}. A rule only chooses: the machine still needs an approval "
                + "on the web, or someone who signs in at it, where the sequence is offered.",
            chosenByModel.Explanation);

        AssignmentRuleView macRule = await administrator.CreatedRuleAsync(RuleRequests.MacRule(byMac.Id, mac));
        MachineSequenceResolution chosenByMac = await administrator.ResolutionAsync(machine.Id);

        Assert.Equal(SequenceResolutionSource.MacRule, chosenByMac.Source);
        Assert.Equal(byMac.Id, chosenByMac.SequenceId);
        Assert.Equal(macRule.Id, chosenByMac.RuleId);
    }

    [Fact]
    public async Task ThePrimaryMacAddressComesBeforeTheOthers()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        SequenceView sequence = await application.RunnableSequenceAsync();
        string primary = RuleRequests.RandomMac();
        string other = RuleRequests.RandomMac();
        using RegisteredMachine machine = await application.RegisterModelAsync("Dell Inc.", RuleRequests.UniqueModel(), macs: [primary, other]);

        AssignmentRuleView byOther = await administrator.CreatedRuleAsync(RuleRequests.MacRule(sequence.Id, other));
        Assert.Equal(byOther.Id, (await administrator.ResolutionAsync(machine.Id)).RuleId);

        AssignmentRuleView byPrimary = await administrator.CreatedRuleAsync(RuleRequests.MacRule(sequence.Id, primary));
        Assert.Equal(byPrimary.Id, (await administrator.ResolutionAsync(machine.Id)).RuleId);
    }

    // Each rule added matches the machine better than the ones before it.
    [Fact]
    public async Task AnExactModelComesBeforeAPrefixAndALongerPrefixBeforeAShorterOne()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        Guid sequence = (await application.RunnableSequenceAsync()).Id;
        string family = $"Lat{Guid.NewGuid():N}";
        using RegisteredMachine machine = await application.RegisterModelAsync("Dell Inc.", $"{family} 5440");

        async Task<Guid> RuleChosenAsync(SaveAssignmentRuleRequest rule)
        {
            AssignmentRuleView created = await administrator.CreatedRuleAsync(rule);
            MachineSequenceResolution resolution = await administrator.ResolutionAsync(machine.Id);

            Assert.Equal(SequenceResolutionSource.ModelRule, resolution.Source);
            Assert.Equal(created.Id, resolution.RuleId);

            return created.Id;
        }

        await RuleChosenAsync(RuleRequests.ModelRule(sequence, $"{family}*"));
        await RuleChosenAsync(RuleRequests.ModelRule(sequence, $"{family} 54*"));
        await RuleChosenAsync(RuleRequests.ModelRule(sequence, $"{family} 54*", "Dell Inc."));
        await RuleChosenAsync(RuleRequests.ModelRule(sequence, $"{family} 5440"));
        Guid best = await RuleChosenAsync(RuleRequests.ModelRule(sequence, $"{family} 5440", "Dell Inc."));

        // Rules for other makers and other models never match.
        await administrator.CreatedRuleAsync(RuleRequests.ModelRule(sequence, $"{family} 5440", "Lenovo"));
        await administrator.CreatedRuleAsync(RuleRequests.ModelRule(sequence, $"{family} 5440 2-in-1"));
        Assert.Equal(best, (await administrator.ResolutionAsync(machine.Id)).RuleId);
    }

    [Fact]
    public async Task MatchesAModelWhateverItsCaseAndSpacing()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        Guid sequence = (await application.RunnableSequenceAsync()).Id;
        string model = RuleRequests.UniqueModel();
        using RegisteredMachine machine = await application.RegisterModelAsync("  dell   inc. ", $"  {model.ToLowerInvariant().Replace(" ", "   ", StringComparison.Ordinal)} ");

        AssignmentRuleView rule = await administrator.CreatedRuleAsync(RuleRequests.ModelRule(sequence, model.ToUpperInvariant(), "DELL INC."));

        Assert.Equal(rule.Id, (await administrator.ResolutionAsync(machine.Id)).RuleId);
    }

    // Firmware of a board nobody filled in says nothing about the machine, so no prefix may match its placeholder.
    [Fact]
    public async Task AMachineThatReportsAPlaceholderMatchesNoModelRule()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        Guid sequence = (await application.RunnableSequenceAsync()).Id;
        await administrator.PostAsync(RuleRequests.Rules, RuleRequests.ModelRule(sequence, "To Be*"));
        using RegisteredMachine machine = await application.RegisterModelAsync("To Be Filled By O.E.M.", "To Be Filled By O.E.M.");

        MachineSequenceResolution resolution = await administrator.ResolutionAsync(machine.Id);

        Assert.Equal(new MachineSequenceResolution(
            SequenceResolutionSource.None,
            null,
            null,
            null,
            0,
            "No rule matches the MAC addresses or the model of this machine, so an operator chooses its sequence."), resolution);
    }

    [Fact]
    public async Task AnAssignmentOnTheWebComesBeforeEveryRule()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        string model = RuleRequests.UniqueModel();
        await administrator.CreatedRuleAsync(RuleRequests.ModelRule((await application.RunnableSequenceAsync()).Id, model));
        using RegisteredMachine machine = await application.RegisterModelAsync("Dell Inc.", model);
        Image image = await application.SeedImageAsync(RandomNumberGenerator.GetBytes(4096));

        (await administrator.PostAsync($"/api/machines/{machine.Id}/deployments", new AssignImageRequest(image.Id, null))).EnsureSuccessStatusCode();
        MachineSequenceResolution resolution = await administrator.ResolutionAsync(machine.Id);

        Assert.Equal(SequenceResolutionSource.Assigned, resolution.Source);
        Assert.Null(resolution.RuleId);
        Assert.StartsWith("administrator-", resolution.Explanation, StringComparison.Ordinal);
        Assert.EndsWith($" assigned {image.Name} on the web, which comes before every rule.", resolution.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AChoiceAtTheMachineComesBeforeEveryRule()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        string operatorName = await application.CreateUserAsync(DdtRoleNames.Operator);
        await administrator.CreatedRuleAsync(RuleRequests.ModelRule((await application.RunnableSequenceAsync()).Id, "Virtual Machine", "Microsoft Corporation"));
        using DeployingMachine machine = await DeployingMachine.SignedInAsync(application, operatorName, [DeployingMachine.Disk(0)]);
        Image image = await application.SeedImageAsync(RandomNumberGenerator.GetBytes(4096));

        (await machine.Agent.PickAsync(machine.Id, machine.Token, new AgentPickRequest(image.Id, 0, null))).EnsureSuccessStatusCode();
        MachineSequenceResolution resolution = await administrator.ResolutionAsync(machine.Id);

        Assert.Equal(SequenceResolutionSource.Console, resolution.Source);
        Assert.Equal($"{operatorName} chose {image.Name} at the machine, which comes before every rule.", resolution.Explanation);
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
