// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Security.Cryptography;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;
using DDT.Server.Deployments;
using DDT.Server.Images;
using Xunit;
using static DDT.Server.Tests.TestReports;

namespace DDT.Server.Tests;

public sealed class RunProgressRestartTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    private async Task<(DeployingMachine Machine, AgentRun Run)> AssignedAsync()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, administrator);
        Image image = await application.SeedImageAsync(RandomNumberGenerator.GetBytes(4096));
        SequenceView sequence = await administrator.CreatedSequenceAsync(SequenceRequests.Minimal(image.Id));

        await administrator.AssignedAsync(machine.Id, sequence.Id);

        return (machine, (await machine.NextAsync()).Run!);
    }

    private async Task<DeploymentStepView> ApplyViewAsync(AgentRun run) =>
        (await (await application.AdministratorAsync()).RunAsync(run.Id)).Steps[1];

    private static StepRunState Partitioned(AgentRun run) => Step(run.Sequence.Steps[0], StepState.Done) with { Pass = 1 };

    private static StepRunState Applying(AgentRun run, int restarts, int pass = 1) =>
        Step(run.Sequence.Steps[1], StepState.Running) with { Pass = pass, Restarts = restarts };

    [Fact]
    public async Task AStepThatRestartsAndGoesOnStaysInItsVisit()
    {
        (DeployingMachine machine, AgentRun run) = await AssignedAsync();
        using DeployingMachine _ = machine;

        await machine.ReportOkAsync(run.Id, Running(Partitioned(run), Applying(run, 0)) with { Percent = 40 });
        DeploymentStepView before = await ApplyViewAsync(run);

        await machine.ReportOkAsync(run.Id, Running(Partitioned(run), Applying(run, 1)) with { Percent = 60 });
        await machine.ReportOkAsync(run.Id, Running(Partitioned(run), Applying(run, 2)) with { Percent = 70 });
        DeploymentStepView after = await ApplyViewAsync(run);

        Assert.Equal(0, before.Restarts);
        Assert.Equal(2, after.Restarts);
        Assert.Equal(StepState.Running, after.State);
        Assert.Equal(1, after.Pass);
        Assert.Equal(70, after.Percent);
        Assert.Equal(before.StartedUtc, after.StartedUtc);
        Assert.Null(after.FinishedUtc);
    }

    [Fact]
    public async Task AReportSentLateDoesNotCountRestartsBack()
    {
        (DeployingMachine machine, AgentRun run) = await AssignedAsync();
        using DeployingMachine _ = machine;

        await machine.ReportOkAsync(run.Id, Running(Partitioned(run), Applying(run, 3)));
        await machine.ReportOkAsync(run.Id, Running(Partitioned(run), Applying(run, 1)));

        Assert.Equal(3, (await ApplyViewAsync(run)).Restarts);
    }

    [Fact]
    public async Task TheRestartsAreKeptWhenTheStepEnds()
    {
        (DeployingMachine machine, AgentRun run) = await AssignedAsync();
        using DeployingMachine _ = machine;

        await machine.ReportOkAsync(run.Id, Running(Partitioned(run), Applying(run, 2)));
        await machine.ReportOkAsync(run.Id, Running(Partitioned(run), Step(run.Sequence.Steps[1], StepState.Done) with { Pass = 1, Restarts = 2 }));

        DeploymentStepView done = await ApplyViewAsync(run);
        Assert.Equal(StepState.Done, done.State);
        Assert.Equal(2, done.Restarts);
    }

    [Fact]
    public async Task ANewVisitOfTheStepStartsItsRestartsOver()
    {
        (DeployingMachine machine, AgentRun run) = await AssignedAsync();
        using DeployingMachine _ = machine;

        await machine.ReportOkAsync(run.Id, Running(Partitioned(run), Applying(run, 2)));
        await machine.ReportOkAsync(run.Id, Running(Partitioned(run), Applying(run, 0, pass: 2)));

        DeploymentStepView visit = await ApplyViewAsync(run);
        Assert.Equal(2, visit.Pass);
        Assert.Equal(0, visit.Restarts);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(DeploymentLimits.MaxStepRestarts + 1)]
    public async Task RestartsThatCannotBeAreRefused(int restarts)
    {
        (DeployingMachine machine, AgentRun run) = await AssignedAsync();
        using DeployingMachine _ = machine;

        await machine.ReportOkAsync(run.Id, Running(Partitioned(run), Applying(run, 1)));
        HttpResponseMessage refused = await machine.ReportAsync(run.Id, Running(Partitioned(run), Applying(run, restarts)));

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(1, (await ApplyViewAsync(run)).Restarts);
    }

    [Fact]
    public async Task TheMostRestartsAStepCanHaveAreTaken()
    {
        (DeployingMachine machine, AgentRun run) = await AssignedAsync();
        using DeployingMachine _ = machine;

        await machine.ReportOkAsync(run.Id, Running(Partitioned(run), Applying(run, DeploymentLimits.MaxStepRestarts)));

        Assert.Equal(DeploymentLimits.MaxStepRestarts, (await ApplyViewAsync(run)).Restarts);
    }
}
