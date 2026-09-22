// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography;
using System.Threading.Channels;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Contracts.Sequences;
using DDT.Server.Authentication;
using DDT.Server.Images;
using DDT.Server.Live;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DDT.Server.Tests;

// The web UI replaces a machine's row with every push, so a push without the deployment would blank the column.
public sealed class DeploymentPushTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    private static async Task<MachineSummary> PushedAsync(ChannelReader<MachineSummary> pushes, Guid machineId, Func<MachineSummary, bool> expected)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        await foreach (MachineSummary pushed in pushes.ReadAllAsync(timeout.Token))
        {
            if (pushed.Id == machineId && expected(pushed))
            {
                return pushed;
            }
        }

        throw new InvalidOperationException("The hub closed.");
    }

    [Fact]
    public async Task EveryPushCarriesTheMachinesDeployment()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        Channel<MachineSummary> pushes = Channel.CreateUnbounded<MachineSummary>();

        await using HubConnection connection = new HubConnectionBuilder()
            .WithUrl(new Uri(application.Server.BaseAddress, "hubs/live"), options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => application.Server.CreateHandler();
                options.Headers["Cookie"] = administrator.Cookies.GetCookieHeader(application.Server.BaseAddress);
            })
            .AddJsonProtocol(json => json.PayloadSerializerOptions = TestJson.Options)
            .Build();

        connection.On<MachineSummary>("machineChanged", machine => pushes.Writer.TryWrite(machine));
        await connection.StartAsync(TestContext.Current.CancellationToken);

        using DeployingMachine machine = await DeployingMachine.RegisterAsync(application, [DeployingMachine.Disk(0)]);
        MachineSummary registered = await PushedAsync(pushes.Reader, machine.Id, m => m.State == MachineState.Pending);

        Assert.Null(registered.Deployment);
        Assert.Equal(1, registered.EligibleDiskCount);
        Assert.Equal("Disk 0: Msft Virtual Disk, 64 GB, SCSI", registered.Disks);

        SequenceView sequence = await administrator.CreatedSequenceAsync(SequenceRequests.ScriptOnly());
        (await administrator.AssignAsync(machine.Id, sequence.Id)).EnsureSuccessStatusCode();
        Guid deployment = (await PushedAsync(pushes.Reader, machine.Id, m => m.Deployment?.State == DeploymentState.Assigned)).Deployment!.Id;

        // A poll after a while records last seen and pushes: the run stays in the row.
        await application.ChangeMachineAsync(machine.Id, m => m.LastSeenUtc = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(1));
        await machine.NextAsync();
        Assert.Equal(deployment, (await PushedAsync(pushes.Reader, machine.Id, m => m.State == MachineState.Approved)).Deployment?.Id);

        (await administrator.EndCurrentAsync(machine.Id)).EnsureSuccessStatusCode();
        MachineSummary cancelled = await PushedAsync(pushes.Reader, machine.Id, m => m.Deployment?.State == DeploymentState.Cancelled);

        Assert.Equal(sequence.Name, cancelled.Deployment?.Title);
        Assert.Equal(1, cancelled.Deployment?.StepCount);

        // Nothing is active any more: every later push carries the one that ended last.
        await application.ChangeMachineAsync(machine.Id, m => m.LastSeenUtc = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(1));
        await machine.NextAsync();
        Assert.Equal(deployment, (await PushedAsync(pushes.Reader, machine.Id, m => m.State == MachineState.Approved && m.Deployment?.State == DeploymentState.Cancelled)).Deployment?.Id);

        await machine.RegisterAgainAsync();
        Assert.Equal(deployment, (await PushedAsync(pushes.Reader, machine.Id, m => m.State == MachineState.Pending)).Deployment?.Id);

        string operatorName = await application.CreateUserAsync(DdtRoleNames.Operator);
        (await machine.Agent.SignInAsync(machine.Id, machine.Token, new AgentSignInRequest(operatorName, DdtApplication.Password, null))).EnsureSuccessStatusCode();
        Assert.Equal(deployment, (await PushedAsync(pushes.Reader, machine.Id, m => m.State == MachineState.Approved)).Deployment?.Id);

        (await administrator.PostAsync($"/api/machines/{machine.Id}/reject")).EnsureSuccessStatusCode();
        Assert.Equal(deployment, (await PushedAsync(pushes.Reader, machine.Id, m => m.State == MachineState.Rejected)).Deployment?.Id);
    }

    // A running run's row shows its step, and every push carries the latest of it.
    [Fact]
    public async Task APushCarriesTheStepTheRunIsAt()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        await using LiveListener listener = await LiveListener.StartAsync(application, administrator);
        ChannelReader<MachineSummary> pushes = listener.Listen<MachineSummary>(LiveEvents.MachineChanged);
        using DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, administrator);
        Image image = await application.SeedImageAsync(RandomNumberGenerator.GetBytes(4096));
        SequenceView sequence = await administrator.CreatedSequenceAsync(SequenceRequests.Minimal(image.Id));

        await administrator.AssignedAsync(machine.Id, sequence.Id);
        AgentRun run = (await machine.NextAsync()).Run!;
        await machine.ReportOkAsync(run.Id, TestReports.Report(
            DeploymentState.Running,
            [TestReports.Step(run.Sequence.Steps[0], StepState.Done), TestReports.Step(run.Sequence.Steps[1], StepState.Running)],
            percent: 35));

        MachineSummary running = await PushedAsync(pushes, machine.Id, m => m.Deployment?.StepName == "Apply");

        Assert.Equal(MachineState.Deploying, running.State);
        Assert.Equal(1, running.Deployment?.StepIndex);
        Assert.Equal(2, running.Deployment?.StepCount);
        Assert.Equal(35, running.Deployment?.Percent);
        Assert.Equal(SequencePhase.WindowsPE, running.Deployment?.Phase);
        Assert.NotNull(running.Deployment?.StartedUtc);

        await machine.ReportOkAsync(run.Id, TestReports.Report(DeploymentState.Failed, [], error: "Apply failed."));

        Assert.Equal("Apply failed.", (await PushedAsync(pushes, machine.Id, m => m.State == MachineState.Failed)).Deployment?.Error);
    }
}
