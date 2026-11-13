// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using DDT.Contracts.Agents;
using DDT.Contracts.Applications;
using DDT.Contracts.Deployments;
using DDT.Contracts.Rules;
using DDT.Contracts.Sequences;
using DDT.Contracts.Values;
using DDT.Server.Authentication;
using DDT.Server.Deployments;
using DDT.Server.Images;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static DDT.Server.Tests.ApplicationRequests;
using static DDT.Server.Tests.Applications.ApplicationRuns;
using static DDT.Server.Tests.TestReports;

namespace DDT.Server.Tests.Applications;

// When a run starts, what a step's value names is appended to its plan: ids from an Applications input's answer, names
// from a rule. Anything that cannot be installed ends the run before a disk is touched.
public sealed class RunApplicationStartTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    private static AgentRunReport Preparing => Report(DeploymentState.Running, []) with { Activity = RunActivity.Preparing };

    private static string RuleName(DeployingMachine machine) => $"Values for {machine.Registration.PrimaryMac}";

    // A rule that gives the machine one value.
    private async Task RuleAsync(DeployingMachine machine, string name, string value) =>
        await (await application.AdministratorAsync()).CreatedRuleAsync(RuleRequests.Rule(
            RuleName(machine),
            new TestCondition(MachineVariableNames.MacAddress, ConditionOperator.Equals, machine.Registration.PrimaryMac),
            values: [new NamedValue(name, value)]));

    private Task<Deployment> StoredAsync(Guid runId) =>
        application.QueryAsync(database => database.Deployments.AsNoTracking().SingleAsync(d => d.Id == runId, TestContext.Current.CancellationToken));

    private async Task<IReadOnlyList<Guid>> PlannedAsync(Guid runId, Guid stepId) =>
        [.. (await application.PlanAsync(runId, stepId) ?? []).Select(planned => planned.Id)];

    private static async Task<string?> RefusedAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        return await TestDatabase.TitleAsync(response);
    }

    [Fact]
    public async Task TheIdsAnInputWasAnsweredWithAreAppendedWhenTheRunStarts()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        ApplicationView fixedApp = await administrator.WithFilesAsync("Fixed");
        ApplicationView extra = await administrator.WithFilesAsync("Extra");
        ApplicationView member = await administrator.CreatedApplicationAsync(Command(Unique("Member")));
        ApplicationBundleView bundle = await administrator.CreatedBundleAsync(Bundle(Unique("Extras"), member.Id));
        InstallApplicationsStep install = Install("Extras", Of(fixedApp));
        InputDeclaration input = new()
        {
            Name = "Extras",
            Label = "Extras",
            Kind = InputKind.Applications,
            AskAt = InputAsk.Web,
            Offered = [Of(extra), Of(bundle)],
            Ticked = [extra.Id],
        };
        SequenceView sequence = await application.SequenceAsync([input], install);

        (DeployingMachine machine, AgentRun run) = await application.AssignedAsync(
            sequence,
            [new InputAnswer("Extras", $"{extra.Id:D};{bundle.Id.ToString("D").ToUpperInvariant()}")]);
        using DeployingMachine held = machine;

        Assert.Equal([fixedApp.Id], await PlannedAsync(run.Id, install.Id));

        await machine.ReportOkAsync(run.Id, Preparing);

        Assert.Equal([fixedApp.Id, extra.Id, member.Id], await PlannedAsync(run.Id, install.Id));
        Assert.Equal(
            new[] { PackageOf(fixedApp), PackageOf(extra) }.Order(),
            (await application.ApplicationArtifactsAsync(run.Id)).Select(artifact => artifact.SourceId).Order());
    }

    [Fact]
    public async Task ARuleNamesApplicationsAndBundlesWhateverTheCaseAndSpacing()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        ApplicationView editor = await administrator.WithFilesAsync("Code Editor");
        ApplicationView runtime = await administrator.WithFilesAsync("Runtime");
        ApplicationView viewer = await administrator.CreatedApplicationAsync(Command(Unique("Viewer"), null, runtime.Id));
        ApplicationBundleView bundle = await administrator.CreatedBundleAsync(Bundle(Unique("Media Kit"), viewer.Id));
        InstallApplicationsStep install = Install("Apps");
        SequenceView sequence = await application.SequenceAsync(null, install);
        string value = $"  {editor.Name.ToLowerInvariant().Replace(" ", "   ", StringComparison.Ordinal)} ;; {bundle.Name.ToUpperInvariant()} ";

        (DeployingMachine machine, AgentRun run) = await application.AssignedAsync(sequence, before: assigned => RuleAsync(assigned, "Apps", value));
        using DeployingMachine held = machine;

        await machine.ReportOkAsync(run.Id, Preparing);

        Assert.Equal([editor.Id, runtime.Id, viewer.Id], await PlannedAsync(run.Id, install.Id));
        Assert.Equal(
            new[] { PackageOf(editor), PackageOf(runtime) }.Order(),
            (await application.ApplicationArtifactsAsync(run.Id)).Select(artifact => artifact.SourceId).Order());
    }

    [Fact]
    public async Task WhatTheStepsOwnListHasIsNotAppendedAgain()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        ApplicationView listed = await administrator.WithFilesAsync("Listed");
        ApplicationView added = await administrator.WithFilesAsync("Added", listed.Id);
        InstallApplicationsStep install = Install("Apps", Of(listed));
        SequenceView sequence = await application.SequenceAsync(null, install);

        (DeployingMachine machine, AgentRun run) = await application.AssignedAsync(
            sequence,
            before: assigned => RuleAsync(assigned, "Apps", $"{added.Name};{listed.Id:D}"));
        using DeployingMachine held = machine;

        await machine.ReportOkAsync(run.Id, Preparing);

        Assert.Equal([listed.Id, added.Id], await PlannedAsync(run.Id, install.Id));
        Assert.Equal(
            new[] { PackageOf(listed), PackageOf(added) }.Order(),
            (await application.ApplicationArtifactsAsync(run.Id)).Select(artifact => artifact.SourceId).Order());
    }

    [Fact]
    public async Task AnUnknownNameEndsTheRunAtItsStartAndSaysWhereItCameFrom()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        ApplicationView known = await administrator.WithFilesAsync("Known");
        InstallApplicationsStep install = Install("Apps", Of(known));
        SequenceView sequence = await application.SequenceAsync(null, install);
        string unknown = $"Nothing {Guid.NewGuid():N}";

        (DeployingMachine machine, AgentRun run) = await application.AssignedAsync(
            sequence,
            before: assigned => RuleAsync(assigned, "Apps", $"{known.Name}; {unknown}"));
        using DeployingMachine held = machine;

        string? error = await RefusedAsync(await machine.ReportAsync(run.Id, Preparing));

        Assert.Equal(
            $"The run did not start. {StepName} is to install {unknown}, which is no application or bundle in the library. " +
            $"Apps came from the rule {RuleName(machine)}.",
            error);
        Deployment failed = await StoredAsync(run.Id);
        Assert.Equal((DeploymentState.Failed, error, (DateTimeOffset?)null), (failed.State, failed.Error, failed.StartedUtc));
        Assert.Equal([known.Id], await PlannedAsync(run.Id, install.Id));
        Assert.Single(await application.ApplicationArtifactsAsync(run.Id));
    }

    [Fact]
    public async Task FilesNoLongerStoredEndTheRunAtItsStart()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        ApplicationView lost = await administrator.WithFilesAsync("Lost");
        SequenceView sequence = await application.SequenceAsync(null, Install(null, Of(lost)));
        (DeployingMachine machine, AgentRun run) = await application.AssignedAsync(sequence);
        using DeployingMachine held = machine;

        File.Delete(application.Services.GetRequiredService<ImageStore>().ObjectPath((await application.FilesOfAsync(lost)).Sha256));
        string? error = await RefusedAsync(await machine.ReportAsync(run.Id, Preparing));

        Assert.Equal($"The run did not start. The files of {lost.Name}, which {StepName} installs, are no longer in the library.", error);
        Assert.Equal(DeploymentState.Failed, (await StoredAsync(run.Id)).State);
    }

    // The machine asks an Applications input by id, so names a rule gives become the ids they stand for.
    [Fact]
    public async Task TheMachineSeesTheEntriesARuleNamesTicked()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        ApplicationView editor = await administrator.CreatedApplicationAsync(Command(Unique("Editor")));
        ApplicationBundleView bundle = await administrator.CreatedBundleAsync(Bundle(Unique("Office"), editor.Id));
        ApplicationView other = await administrator.CreatedApplicationAsync(Command(Unique("Other")));
        InputDeclaration input = new()
        {
            Name = "Extras",
            Label = "Extras",
            Kind = InputKind.Applications,
            AskAt = InputAsk.Machine,
            Offered = [Of(editor), Of(bundle), Of(other)],
            Ticked = [other.Id],
        };
        SequenceView sequence = await application.SequenceAsync([input], Install("Extras"));
        using DeployingMachine machine = await DeployingMachine.SignedInAsync(
            application,
            await application.CreateUserAsync(DdtRoleNames.Operator),
            [DeployingMachine.Disk(0)]);
        await RuleAsync(machine, "Extras", $"{bundle.Name.ToLowerInvariant()} ; {editor.Id:D}");

        AgentSequenceChoice choice = Assert.Single(
            await RegisteredMachine.ReadAsync<IReadOnlyList<AgentSequenceChoice>>(await machine.Agent.SequencesAsync(machine.Id, machine.Token)),
            listed => listed.Id == sequence.Id);

        Assert.Equal($"{bundle.Id:D};{editor.Id:D}", Assert.Single(choice.Inputs ?? []).Default);
    }
}
