// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Security.Cryptography;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Server.Deployments;
using DDT.Server.Images;
using DDT.Server.Machines;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DDT.Server.Tests;

public sealed class DeploymentReportTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    private static readonly AgentLogBatch s_oneLine = new([new AgentLogLine(DateTimeOffset.UtcNow, AgentLogLevel.Information, "x")]);

    private async Task<DeployingMachine> ApprovedAsync() =>
        await DeployingMachine.ApprovedAsync(application, await application.AdministratorAsync());

    // An image assigned on the web, as the agent finds it at its next poll.
    private async Task<Guid> AssignAsync(Guid machineId)
    {
        SignedInClient administrator = await application.AdministratorAsync();
        Image image = await application.SeedImageAsync(RandomNumberGenerator.GetBytes(4096));

        MachineSummary summary = await RegisteredMachine.ReadAsync<MachineSummary>(
            await administrator.PostAsync($"/api/machines/{machineId}/deployments", new AssignImageRequest(image.Id, null)));

        return summary.Deployment!.Id;
    }

    private Task<Deployment> StoredAsync(Guid deploymentId) =>
        application.QueryAsync(database => database.Deployments.AsNoTracking().SingleAsync(d => d.Id == deploymentId, TestContext.Current.CancellationToken));

    private Task<List<string>> AuditActionsAsync(Guid deploymentId)
    {
        string subject = deploymentId.ToString("D");

        return application.QueryAsync(database => database.AuditEvents
            .Where(e => e.SubjectId == subject)
            .OrderBy(e => e.Id)
            .Select(e => e.Action)
            .ToListAsync(TestContext.Current.CancellationToken));
    }

    private Task<string?> AuditDetailAsync(Guid deploymentId, string action)
    {
        string subject = deploymentId.ToString("D");

        return application.QueryAsync(database => database.AuditEvents
            .Where(e => e.SubjectId == subject && e.Action == action)
            .Select(e => e.Detail)
            .SingleAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ADeploymentRunsThroughItsStepsToDone()
    {
        using DeployingMachine machine = await ApprovedAsync();
        Guid deploymentId = await AssignAsync(machine.Id);

        await machine.ReportOkAsync(deploymentId, DeploymentState.Running, DeploymentStep.Partition);

        Deployment running = await StoredAsync(deploymentId);
        Assert.Equal(DeploymentState.Running, running.State);
        Assert.NotNull(running.StartedUtc);
        Assert.Equal(MachineState.Deploying, (await application.MachineAsync(machine.Id)).State);

        // The token each report hands back is the session token of a deploying machine.
        Assert.Equal(HttpStatusCode.NoContent, (await machine.Agent.LogAsync(machine.Id, machine.Token, s_oneLine)).StatusCode);

        await machine.ReportOkAsync(deploymentId, DeploymentState.Running, DeploymentStep.Download, 55);
        await machine.ReportOkAsync(deploymentId, DeploymentState.Running, DeploymentStep.Download, 60);

        HttpResponseMessage backwards = await machine.ReportAsync(deploymentId, DeploymentState.Running, DeploymentStep.Partition, 100);
        Assert.Equal(HttpStatusCode.Conflict, backwards.StatusCode);
        Assert.Equal(
            "The deployment is already at the Download step and cannot go back to the Partition step. " +
            "Ask the server for the current deployment and report its current step.",
            await TestDatabase.TitleAsync(backwards));

        AgentNextResult deploying = await machine.NextAsync();
        Assert.Equal(MachineState.Deploying, deploying.State);
        Assert.Equal(DeploymentState.Running, deploying.Deployment?.State);

        await machine.ReportOkAsync(deploymentId, DeploymentState.Running, DeploymentStep.Unattend, 0);
        await machine.ReportOkAsync(deploymentId, DeploymentState.Done, DeploymentStep.Reboot, 100);

        Deployment done = await StoredAsync(deploymentId);
        Machine stored = await application.MachineAsync(machine.Id);

        Assert.Equal(DeploymentState.Done, done.State);
        Assert.Equal(DeploymentStep.Reboot, done.Step);
        Assert.Equal(100, done.Percent);
        Assert.NotNull(done.FinishedUtc);
        Assert.Equal(MachineState.Done, stored.State);
        Assert.Null(stored.ActiveDeploymentId);
        Assert.Equal(
            [AuditActions.DeploymentAssigned, AuditActions.DeploymentStarted, AuditActions.DeploymentDone],
            await AuditActionsAsync(deploymentId));

        // Done takes no token at all: the agent reboots, and anything asking afterwards is not that agent.
        Assert.Equal(HttpStatusCode.Unauthorized, (await machine.Agent.NextAsync(machine.Id, machine.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await machine.Agent.LogAsync(machine.Id, machine.Token, s_oneLine)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await machine.ReportAsync(deploymentId, DeploymentState.Done, DeploymentStep.Reboot, 100)).StatusCode);
    }

    [Fact]
    public async Task ACheckBeforeTheDiskWasTouchedFailsTheAssignment()
    {
        using DeployingMachine machine = await ApprovedAsync();
        Guid deploymentId = await AssignAsync(machine.Id);

        await machine.ReportOkAsync(deploymentId, DeploymentState.Failed, DeploymentStep.Partition, 0, "No internal disk was found.");

        Deployment failed = await StoredAsync(deploymentId);

        Assert.Equal(DeploymentState.Failed, failed.State);
        Assert.Null(failed.StartedUtc);
        Assert.NotNull(failed.FinishedUtc);
        Assert.Equal("No internal disk was found.", failed.Error);
        Assert.Equal(MachineState.Failed, (await machine.NextAsync()).State);
        Assert.Equal([AuditActions.DeploymentAssigned, AuditActions.DeploymentFailed], await AuditActionsAsync(deploymentId));

        // The agent has to name a step, but none ran: the disk was never touched.
        Assert.Null(failed.Step);
        Assert.Equal(0, failed.Percent);
        Assert.Equal(
            $"{failed.ImageName} on machine {machine.Id:D}: No internal disk was found.",
            await AuditDetailAsync(deploymentId, AuditActions.DeploymentFailed));
    }

    [Fact]
    public async Task AFailureAfterTheStartNamesItsStep()
    {
        using DeployingMachine machine = await ApprovedAsync();
        Guid deploymentId = await AssignAsync(machine.Id);

        await machine.ReportOkAsync(deploymentId, DeploymentState.Running, DeploymentStep.Partition);
        await machine.ReportOkAsync(deploymentId, DeploymentState.Failed, DeploymentStep.Download, 40, "The image download stopped.");

        Deployment failed = await StoredAsync(deploymentId);

        Assert.Equal(DeploymentStep.Download, failed.Step);
        Assert.Equal(40, failed.Percent);
        Assert.Equal(
            $"{failed.ImageName} on machine {machine.Id:D} at Download: The image download stopped.",
            await AuditDetailAsync(deploymentId, AuditActions.DeploymentFailed));
    }

    [Fact]
    public async Task ARepeatedFailureIsAnsweredLikeTheFirst()
    {
        using DeployingMachine machine = await ApprovedAsync();
        Guid deploymentId = await AssignAsync(machine.Id);

        await machine.ReportOkAsync(deploymentId, DeploymentState.Running, DeploymentStep.Partition);
        await machine.ReportOkAsync(deploymentId, DeploymentState.Failed, DeploymentStep.Apply, 12, "Apply failed.");

        // The response to the first one was lost.
        await machine.ReportOkAsync(deploymentId, DeploymentState.Failed, DeploymentStep.Apply, 12, "Apply failed.");

        Assert.Equal(HttpStatusCode.Conflict, (await machine.ReportAsync(deploymentId, DeploymentState.Running, DeploymentStep.Apply, 13)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await machine.ReportAsync(deploymentId, DeploymentState.Done, DeploymentStep.Reboot, 100)).StatusCode);
        Assert.Equal("Apply failed.", (await StoredAsync(deploymentId)).Error);
        Assert.Single(await AuditActionsAsync(deploymentId), action => action == AuditActions.DeploymentFailed);
    }

    [Fact]
    public async Task RefusesTransitionsThatDoNotExist()
    {
        using DeployingMachine machine = await ApprovedAsync();
        Guid deploymentId = await AssignAsync(machine.Id);

        HttpResponseMessage doneBeforeStarted = await machine.ReportAsync(deploymentId, DeploymentState.Done, DeploymentStep.Reboot, 100);
        Assert.Equal(HttpStatusCode.Conflict, doneBeforeStarted.StatusCode);
        Assert.Equal(
            "A deployment that is assigned cannot be reported as done. Ask the server for the current deployment and report its current step.",
            await TestDatabase.TitleAsync(doneBeforeStarted));
        Assert.Equal(HttpStatusCode.Conflict, (await machine.ReportAsync(deploymentId, DeploymentState.Assigned, DeploymentStep.Partition)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await machine.ReportAsync(deploymentId, DeploymentState.Cancelled, DeploymentStep.Partition)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await machine.ReportAsync(Guid.NewGuid(), DeploymentState.Running, DeploymentStep.Partition)).StatusCode);

        // Another machine's deployment is not this machine's to report on.
        using DeployingMachine other = await ApprovedAsync();
        Guid otherDeployment = await AssignAsync(other.Id);

        Assert.Equal(HttpStatusCode.NotFound, (await machine.ReportAsync(otherDeployment, DeploymentState.Running, DeploymentStep.Partition)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await machine.Agent.ReportAsync(other.Id, machine.Token, otherDeployment, new AgentDeploymentReport(DeploymentState.Running, DeploymentStep.Partition, 0, null))).StatusCode);
        Assert.Equal(DeploymentState.Assigned, (await StoredAsync(otherDeployment)).State);
    }

    [Fact]
    public async Task BoundsWhatTheAgentReports()
    {
        using DeployingMachine machine = await ApprovedAsync();
        Guid deploymentId = await AssignAsync(machine.Id);

        await machine.ReportOkAsync(deploymentId, DeploymentState.Running, DeploymentStep.Download, 250);
        Assert.Equal(100, (await StoredAsync(deploymentId)).Percent);

        await machine.ReportOkAsync(deploymentId, DeploymentState.Running, DeploymentStep.Apply, -5);
        Assert.Equal(0, (await StoredAsync(deploymentId)).Percent);

        await machine.ReportOkAsync(deploymentId, DeploymentState.Failed, DeploymentStep.Apply, 1, "Broken\0 " + new string('e', 2000));

        string error = (await StoredAsync(deploymentId)).Error!;
        Assert.Equal(DeploymentLimits.MaxErrorLength, error.Length);
        Assert.StartsWith("Broken eee", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFailureWithoutAReasonStillSaysSomething()
    {
        using DeployingMachine machine = await ApprovedAsync();
        Guid deploymentId = await AssignAsync(machine.Id);

        await machine.ReportOkAsync(deploymentId, DeploymentState.Failed, DeploymentStep.Partition, 0, " ");

        Assert.Equal("The agent reported a failure without saying why.", (await StoredAsync(deploymentId)).Error);
    }

    [Fact]
    public async Task AReportCountsAsSeeingTheMachine()
    {
        using DeployingMachine machine = await ApprovedAsync();
        Guid deploymentId = await AssignAsync(machine.Id);
        DateTimeOffset longAgo = DateTimeOffset.UtcNow - TimeSpan.FromHours(1);

        await application.ChangeMachineAsync(machine.Id, m => m.LastSeenUtc = longAgo);
        await machine.ReportOkAsync(deploymentId, DeploymentState.Running, DeploymentStep.Partition);

        Assert.True((await application.MachineAsync(machine.Id)).LastSeenUtc > longAgo + TimeSpan.FromMinutes(59));
    }

    [Fact]
    public async Task AWaitingMachineLearnsNothingAboutItsAssignment()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine machine = await DeployingMachine.RegisterAsync(application);
        Image image = await application.SeedImageAsync(RandomNumberGenerator.GetBytes(4096));

        await application.ChangeMachineAsync(machine.Id, m => m.LastSeenUtc = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(10));
        (await administrator.PostAsync($"/api/machines/{machine.Id}/deployments", new AssignImageRequest(image.Id, null))).EnsureSuccessStatusCode();

        AgentNextResult next = await machine.NextAsync();

        Assert.Equal(MachineState.Pending, next.State);
        Assert.Null(next.Deployment);
        Assert.False(next.CanPickImage);
    }
}
