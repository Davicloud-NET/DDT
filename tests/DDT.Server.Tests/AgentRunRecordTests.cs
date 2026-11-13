// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;
using DDT.Contracts.Settings;
using DDT.Server.Certificates;
using DDT.Server.Images;
using DDT.Server.Settings;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DDT.Server.Tests;

// What the agent's deployment record in Windows takes from the run it is handed.
public sealed class AgentRunRecordTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    [Fact]
    public async Task TheRunNamesItsSequenceRevisionAndServer()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, administrator);
        Image image = await application.SeedImageAsync(RandomNumberGenerator.GetBytes(4096), "x64");
        SequenceView sequence = await administrator.CreatedSequenceAsync(SequenceRequests.Minimal(image.Id));
        await administrator.AssignedAsync(machine.Id, sequence.Id);

        AgentRun handed = (await machine.NextAsync()).Run!;

        Assert.Equal((sequence.Id, 1L, ServerNames.DnsName(), false), (handed.SequenceId, handed.SequenceRevision, handed.ServerName, handed.SkipDeploymentRecord));
        Assert.Null(handed.ImageName);
    }

    [Fact]
    public async Task TheImageIsTheOneAStepApplied()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, administrator);
        Image image = await application.SeedImageAsync(RandomNumberGenerator.GetBytes(4096), "x64");
        SequenceView sequence = await administrator.CreatedSequenceAsync(SequenceRequests.Minimal(image.Id));
        DeploymentSummary run = await administrator.AssignedAsync(machine.Id, sequence.Id);
        Guid apply = sequence.Definition.Steps[1].Id;

        await application.QueryAsync(async database =>
        {
            Deployments.DeploymentStep step = await database.DeploymentSteps.SingleAsync(s => s.DeploymentId == run.Id && s.StepId == apply, TestContext.Current.CancellationToken);
            step.State = StepState.Done;
            (await database.Deployments.SingleAsync(d => d.Id == run.Id, TestContext.Current.CancellationToken)).State = DeploymentState.Running;

            return await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        });

        AgentRun handed = (await machine.NextAsync()).Run!;

        Assert.Equal(image.Name, handed.ImageName);
    }

    [Fact]
    public async Task WriteRecordOffTellsTheAgentToLeaveTheRecordOut()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        await administrator.SavedAsync<DeploymentSettings>(SettingsSectionNames.Deployment, values => values with { WriteRecord = false });

        try
        {
            using DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, administrator);
            Image image = await application.SeedImageAsync(RandomNumberGenerator.GetBytes(4096), "x64");
            SequenceView sequence = await administrator.CreatedSequenceAsync(SequenceRequests.Minimal(image.Id));
            await administrator.AssignedAsync(machine.Id, sequence.Id);

            Assert.True((await machine.NextAsync()).Run!.SkipDeploymentRecord);
        }
        finally
        {
            await administrator.SavedAsync<DeploymentSettings>(SettingsSectionNames.Deployment, values => values with { WriteRecord = true });
        }
    }
}
