// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Security.Cryptography;
using System.Threading.Channels;
using DDT.Contracts.Agents;
using DDT.Contracts.Applications;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;
using DDT.Server.Authentication;
using DDT.Server.Deployments;
using DDT.Server.Images;
using DDT.Server.Live;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DDT.Server.Tests.ApplicationRequests;

namespace DDT.Server.Tests.Applications;

// Sequences that install applications: what the library says about them, what they keep from being deleted, and how a
// machine is asked an Applications input.
public sealed class SequenceApplicationTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    private static InstallApplicationsStep Install(params ApplicationReference[] applications) =>
        new() { Id = Guid.NewGuid(), Name = "Install the tools", Applications = applications };

    private static ApplicationReference Named(ApplicationView view) => new(view.Id, ApplicationReferenceKind.Application);

    private static ApplicationReference Named(ApplicationBundleView view) => new(view.Id, ApplicationReferenceKind.Bundle);

    private static InputDeclaration Offering(InputAsk askAt, params ApplicationReference[] offered) => new()
    {
        Name = "Extras",
        Label = "Extras",
        Kind = InputKind.Applications,
        AskAt = askAt,
        Offered = offered,
        Ticked = [offered[0].Id],
    };

    private async Task<SequenceDefinition> InstallingAsync(SequenceStep step, params InputDeclaration[] inputs)
    {
        Image image = await application.SeedImageAsync(RandomNumberGenerator.GetBytes(4096));

        return SequenceRequests.Minimal(image.Id) with
        {
            Steps = [.. SequenceRequests.Minimal(image.Id).Steps, step],
            Inputs = inputs.Length > 0 ? inputs : null,
        };
    }

    private async Task<SequenceValidation> ValidateAsync(SequenceDefinition definition) =>
        await RegisteredMachine.ReadAsync<SequenceValidation>(
            await (await application.AdministratorAsync()).PostAsync($"{SequenceRequests.Sequences}/validate", definition));

    [Fact]
    public async Task AnEntryNoLongerInTheLibraryKeepsTheSequenceADraft()
    {
        InstallApplicationsStep install = Install(new ApplicationReference(Guid.NewGuid(), ApplicationReferenceKind.Application));
        SequenceDefinition definition = await InstallingAsync(
            install,
            Offering(InputAsk.Web, new ApplicationReference(Guid.NewGuid(), ApplicationReferenceKind.Bundle)));

        SequenceValidation validation = await ValidateAsync(definition);

        Assert.Contains(validation.Problems, problem =>
            (problem.StepId, problem.Field, problem.Code) == (install.Id, "applications[0]", "sequence.stepApplicationGone")
            && problem.Message.Contains(install.Name, StringComparison.Ordinal));
        Assert.Contains(validation.Problems, problem =>
            (problem.StepId, problem.Field, problem.Code) == (null, "inputs[0].offered[0]", "sequence.inputApplicationGone"));
    }

    [Fact]
    public async Task WingetThroughABundleAndADependencyNeedsAnAnswerFile()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        ApplicationView winget = await administrator.CreatedApplicationAsync(
            Command(Unique("Terminal")) with { Kind = ApplicationKind.Winget, Command = null, WingetId = "Microsoft.WindowsTerminal" });
        ApplicationView tools = await administrator.CreatedApplicationAsync(Command(Unique("Tools"), null, winget.Id));
        ApplicationBundleView bundle = await administrator.CreatedBundleAsync(Bundle(Unique("Desk"), tools.Id));
        InstallApplicationsStep install = Install(Named(bundle));
        SequenceDefinition definition = await InstallingAsync(install);

        SequenceValidation without = await ValidateAsync(definition);
        SequenceValidation with = await ValidateAsync(definition with
        {
            Steps = [.. definition.Steps.Take(2), new WriteUnattendStep { Id = Guid.NewGuid(), Name = "Answer file" }, install],
        });

        SequenceProblem warning = Assert.Single(without.Warnings, problem => problem.Code == "sequence.wingetNeedsSession");
        Assert.Equal((install.Id, "applications"), (warning.StepId, warning.Field));
        Assert.Contains(winget.Name, warning.Message, StringComparison.Ordinal);
        Assert.Empty(without.Problems);
        Assert.DoesNotContain(with.Warnings, problem => problem.Code == "sequence.wingetNeedsSession");
    }

    [Fact]
    public async Task ASequenceThatNamesAnEntryIsItsUserAndKeepsItInTheLibrary()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        ApplicationView app = await administrator.CreatedApplicationAsync(Command(Unique("Reader")));
        ApplicationBundleView bundle = await administrator.CreatedBundleAsync(Bundle(Unique("Office"), app.Id));
        SequenceView sequence = await administrator.CreatedSequenceAsync(await InstallingAsync(Install(Named(app)), Offering(InputAsk.Web, Named(bundle))));

        Assert.Contains(new ApplicationUse(sequence.Id, sequence.Name, ApplicationUseKind.Sequence), (await administrator.ApplicationAsync(app.Id)).UsedBy);
        Assert.Equal(
            [new ApplicationUse(sequence.Id, sequence.Name, ApplicationUseKind.Sequence)],
            Assert.Single(
                await RegisteredMachine.ReadAsync<IReadOnlyList<ApplicationBundleView>>(await administrator.GetAsync(BundlesPath)),
                listed => listed.Id == bundle.Id).UsedBy);

        HttpResponseMessage keptApplication = await administrator.DeleteAsync($"{ApplicationsPath}/{app.Id:D}");
        HttpResponseMessage keptBundle = await administrator.DeleteAsync($"{BundlesPath}/{bundle.Id:D}");

        Assert.Equal((HttpStatusCode.Conflict, "application.inUse"), (keptApplication.StatusCode, await CodeAsync(keptApplication)));
        Assert.Equal((HttpStatusCode.Conflict, "application.bundleInUse"), (keptBundle.StatusCode, await CodeAsync(keptBundle)));

        (await administrator.SaveSequenceAsync(sequence, SequenceRequests.Minimal((await application.SeedImageAsync(RandomNumberGenerator.GetBytes(4096))).Id)))
            .EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.NoContent, (await administrator.DeleteAsync($"{BundlesPath}/{bundle.Id:D}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await administrator.DeleteAsync($"{ApplicationsPath}/{app.Id:D}")).StatusCode);
    }

    [Fact]
    public async Task ASequenceSaveThatChangesWhoUsesWhatPushesTheEntries()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using SignedInClient viewer = await application.SignInAsync(DdtRoleNames.Viewer);
        await using LiveListener live = await LiveListener.StartAsync(application, viewer);
        ChannelReader<ApplicationView> changed = live.Listen<ApplicationView>(LiveEvents.ApplicationChanged);
        ChannelReader<ApplicationBundleView> bundleChanged = live.Listen<ApplicationBundleView>(LiveEvents.ApplicationBundleChanged);
        ApplicationView app = await administrator.CreatedApplicationAsync(Command(Unique("Viewer")));
        ApplicationBundleView bundle = await administrator.CreatedBundleAsync(Bundle(Unique("Media"), app.Id));
        SequenceDefinition definition = await InstallingAsync(Install(Named(app), Named(bundle)));

        SequenceView sequence = await administrator.CreatedSequenceAsync(definition);
        await LiveListener.NextAsync(changed, view => view.Id == app.Id && view.UsedBy.Any(use => use.Id == sequence.Id));
        await LiveListener.NextAsync(bundleChanged, view => view.Id == bundle.Id && view.UsedBy.Any(use => use.Id == sequence.Id));

        string renamed = $"Renamed {Guid.NewGuid():N}";
        (await administrator.SaveSequenceAsync(sequence, definition, renamed)).EnsureSuccessStatusCode();
        await LiveListener.NextAsync(bundleChanged, view => view.Id == bundle.Id && view.UsedBy.Any(use => use.Name == renamed));

        Assert.Equal(HttpStatusCode.NoContent, (await administrator.DeleteAsync($"{SequenceRequests.Sequences}/{sequence.Id:D}")).StatusCode);
        await LiveListener.NextAsync(bundleChanged, view => view.Id == bundle.Id && view.UsedBy.Count == 0);
        await LiveListener.NextAsync(changed, view => view.Id == app.Id && view.UsedBy.All(use => use.Kind != ApplicationUseKind.Sequence));
    }

    [Fact]
    public async Task TheMachineAsksAnApplicationsInputAsAMultipleChoiceOfIdsAndNames()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        string operatorName = await application.CreateUserAsync(DdtRoleNames.Operator);
        ApplicationView app = await administrator.CreatedApplicationAsync(Command(Unique("Editor")));
        ApplicationBundleView bundle = await administrator.CreatedBundleAsync(Bundle(Unique("Design"), app.Id));
        SequenceView sequence = await administrator.CreatedSequenceAsync(
            await InstallingAsync(Install() with { Variable = "Extras" }, Offering(InputAsk.Machine, Named(bundle), Named(app))));
        using DeployingMachine machine = await DeployingMachine.SignedInAsync(application, operatorName, [DeployingMachine.Disk(0)]);

        AgentSequenceChoice choice = Assert.Single(
            await RegisteredMachine.ReadAsync<IReadOnlyList<AgentSequenceChoice>>(await machine.Agent.SequencesAsync(machine.Id, machine.Token)),
            listed => listed.Id == sequence.Id);
        AgentInput input = Assert.Single(choice.Inputs ?? []);

        Assert.Equal(InputKind.MultiChoice, input.Kind);
        Assert.Equal([new InputChoice(bundle.Id.ToString("D"), bundle.Name), new InputChoice(app.Id.ToString("D"), app.Name)], input.Choices);
        Assert.Equal(bundle.Id.ToString("D"), input.Default);

        HttpResponseMessage byName = await machine.Agent.PickRunAsync(
            machine.Id,
            machine.Token,
            new AgentRunRequest(sequence.Id, 0, "APPS-1", Answers: [new InputAnswer("Extras", app.Name)]));
        AgentRun run = await RegisteredMachine.ReadAsync<AgentRun>(await machine.Agent.PickRunAsync(
            machine.Id,
            machine.Token,
            new AgentRunRequest(sequence.Id, 0, "APPS-1", Answers: [new InputAnswer("Extras", $"{app.Id:D}; {bundle.Id.ToString("D").ToUpperInvariant()}")])));

        Assert.Equal(HttpStatusCode.BadRequest, byName.StatusCode);
        Assert.Equal(6, run.Sequence.Version);
        Assert.Equal(InputKind.Applications, Assert.Single(run.Sequence.Inputs ?? []).Kind);
        Deployment stored = await application.QueryAsync(database =>
            database.Deployments.AsNoTracking().SingleAsync(d => d.Id == run.Id, TestContext.Current.CancellationToken));
        Assert.Equal($"{app.Id:D};{bundle.Id:D}", Assert.Single(RunAnswer.Read(stored.Answers)).Value);
    }

    [Fact]
    public async Task AnAgentOfVersion5IsNeitherOfferedNorHandedASequenceThatInstallsApplications()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        ApplicationView app = await administrator.CreatedApplicationAsync(Command(Unique("Agent5")));
        SequenceView sequence = await administrator.CreatedSequenceAsync(await InstallingAsync(Install(Named(app))));
        using DeployingMachine picking = await DeployingMachine.SignedInAsync(
            application,
            await application.CreateUserAsync(DdtRoleNames.Operator),
            [DeployingMachine.Disk(0)],
            sequenceVersion: 5);
        using DeployingMachine machine = await DeployingMachine.RegisterAsync(application, [DeployingMachine.Disk(0)], sequenceVersion: 5);
        (await administrator.PostAsync($"/api/machines/{machine.Id}/approve")).EnsureSuccessStatusCode();
        await machine.NextAsync();

        Assert.DoesNotContain(
            await RegisteredMachine.ReadAsync<IReadOnlyList<AgentSequenceChoice>>(await picking.Agent.SequencesAsync(picking.Id, picking.Token)),
            listed => listed.Id == sequence.Id);

        DeploymentSummary assigned = await administrator.AssignedAsync(machine.Id, sequence.Id, "APPS-5");
        AgentNextResult next = await machine.NextAsync();

        Assert.Null(next.Run);
        Assert.StartsWith($"{assigned.Title} needs a newer agent", next.WaitReason, StringComparison.Ordinal);
    }
}
