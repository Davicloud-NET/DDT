// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;
using DDT.Server.Deployments;
using DDT.Server.Images;
using Xunit;
using static DDT.Server.Tests.TestReports;

namespace DDT.Server.Tests;

// What the running step says it does now: kept like its percent, cut to its column, gone once the step ends.
public sealed class RunStepDetailTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    [Fact]
    public async Task TheRunShowsTheRunningStepsDetailUntilItEnds()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, administrator);
        Image image = await application.SeedImageAsync(RandomNumberGenerator.GetBytes(4096));
        SequenceView sequence = await administrator.CreatedSequenceAsync(SequenceRequests.Minimal(image.Id));
        await administrator.AssignedAsync(machine.Id, sequence.Id);
        AgentRun run = (await machine.NextAsync()).Run!;
        StepRunState partitioned = Step(run.Sequence.Steps[0], StepState.Done) with { Pass = 1 };
        StepRunState applying = Step(run.Sequence.Steps[1], StepState.Running) with { Pass = 1 };

        await machine.ReportOkAsync(run.Id, Running(partitioned, applying) with { StepDetail = "A NUL\0 " + new string('x', 300) });
        DeploymentSummary detailed = (await administrator.RunAsync(run.Id)).Summary;

        await machine.ReportOkAsync(run.Id, Running(partitioned, applying with { State = StepState.Done }) with { StepDetail = "Left over" });
        DeploymentSummary ended = (await administrator.RunAsync(run.Id)).Summary;

        Assert.Equal(DeploymentLimits.MaxStepDetailLength, detailed.StepDetail?.Length);
        Assert.StartsWith("A NUL x", detailed.StepDetail, StringComparison.Ordinal);
        Assert.Null(ended.StepDetail);
    }
}
