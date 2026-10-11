// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Security.Cryptography;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Contracts.Sequences;
using DDT.Server.Deployments;
using DDT.Server.Images;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DDT.Server.Tests.TestReports;

namespace DDT.Server.Tests;

// A machine starts the Linux boot environment again while its run is handed over to the installed Linux. With the
// run's token its agent says so and the server counts it. Without the token the registration ends the run, as before.
public sealed class LinuxBootReturnTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    private const string WithoutToken = "The machine started again during the run, without the run's token, so the run could not continue.";

    private const string StartedOver =
        "The run was ended at the machine. It started from the network while its run went on in the installed Linux, and someone " +
        "there pressed a key to start over.";

    // A run the Linux boot environment has handed over, and the machine that still holds its token.
    private async Task<(DeployingMachine Machine, AgentRun Run)> HandedOverAsync(string name)
    {
        SignedInClient administrator = await application.AdministratorAsync();
        Image image = await application.SeedRawImageAsync(RandomNumberGenerator.GetBytes(4096), name: $"noble {Guid.NewGuid():N}");
        SequenceView sequence = await administrator.CreatedSequenceAsync(SequenceRequests.LinuxWithInstalledSteps(image.Id));
        DeployingMachine machine = await DeployingMachine.RegisterAsync(application, DeployingMachine.LinuxBoot());
        (await administrator.PostAsync($"/api/machines/{machine.Id}/approve")).EnsureSuccessStatusCode();
        await machine.NextAsync();
        await administrator.AssignedAsync(machine.Id, sequence.Id, name);
        AgentRun run = Assert.IsType<AgentRun>((await machine.NextAsync()).Run);
        StepRunState[] written = [Step(run.Sequence.Steps[0], StepState.Done), Step(run.Sequence.Steps[1], StepState.Done)];

        await machine.ReportOkAsync(run.Id, Report(DeploymentState.Running, written) with { Phase = SequencePhase.LinuxBoot, Activity = RunActivity.HandingOver });
        await machine.ReportOkAsync(run.Id, Returned(null) with { Steps = written });

        return (machine, run);
    }

    // What the boot agent reports when it sends the machine back to its disk: no step moves, and the count if it has one.
    private static AgentRunReport Returned(int? returns) =>
        new(DeploymentState.Running, SequencePhase.Linux, [], null, 0, RunActivity.Restarting, null, BootReturns: returns);

    // The boot environment registers with the token the seed still holds, and keeps the tokens the server answers with.
    private static async Task<(AgentRegistrationResult Registered, string Token)> ReturnAsync(DeployingMachine machine)
    {
        AgentRegistrationResult registered = await RegisteredMachine.ReadAsync<AgentRegistrationResult>(
            await machine.Agent.RegisterAsync(machine.Registration with { RunToken = machine.RunToken }));

        return (registered, Assert.IsType<string>(registered.Token));
    }

    private async Task<MachineSummary> ShownAsync(Guid machineId) => Assert.Single(
        await RegisteredMachine.ReadAsync<IReadOnlyList<MachineSummary>>(await (await application.AdministratorAsync()).GetAsync("/api/machines")),
        shown => shown.Id == machineId);

    private Task<Deployment> StoredAsync(Guid runId) =>
        application.QueryAsync(database => database.Deployments.AsNoTracking().SingleAsync(d => d.Id == runId, TestContext.Current.CancellationToken));

    [Fact]
    public async Task CountsEachReturnTheBootAgentReportsAndShowsItUntilTheInstalledLinuxCalls()
    {
        (DeployingMachine machine, AgentRun run) = await HandedOverAsync("LINUX-60");
        using DeployingMachine owned = machine;
        MachineSummary handedOver = await ShownAsync(machine.Id);
        Assert.NotNull(handedOver.HandedOverUtc);
        Assert.Null(handedOver.BootReturns);

        // The registration with the run's token goes on with the run, and says that nothing was counted yet.
        (AgentRegistrationResult first, string token) = await ReturnAsync(machine);
        Assert.Equal((run.Id, MachineState.Deploying), (first.RunId, first.State));
        Assert.Null(first.BootReturns);
        Assert.Equal(DeploymentState.Running, (await StoredAsync(run.Id)).State);

        (await machine.Agent.RunReportAsync(machine.Id, token, run.Id, Returned(1))).EnsureSuccessStatusCode();
        MachineSummary once = await ShownAsync(machine.Id);
        Assert.Equal((1, (DateTimeOffset?)(await StoredAsync(run.Id)).UpdatedUtc), (once.BootReturns, once.HandedOverUtc));
        Assert.Equal((RunActivity.Restarting, SequencePhase.Linux, AgentEnvironment.LinuxBoot), (once.Deployment?.Activity, once.Deployment?.Phase, once.AgentEnvironment));

        // The next start of the boot environment is told the count, and adds to it.
        (AgentRegistrationResult second, token) = await ReturnAsync(machine);
        Assert.Equal((run.Id, 1), (second.RunId, second.BootReturns));
        (await machine.Agent.RunReportAsync(machine.Id, token, run.Id, Returned(2))).EnsureSuccessStatusCode();
        Assert.Equal(2, (await ShownAsync(machine.Id)).BootReturns);

        // The agent's first call from the installed Linux ends the wait, and with it what the web says of the returns.
        AgentRegistrationResult installed = await RegisteredMachine.ReadAsync<AgentRegistrationResult>(await machine.Agent.RegisterAsync(
            machine.Registration with { RunToken = machine.RunToken, Environment = AgentEnvironment.Linux }));
        MachineSummary called = await ShownAsync(machine.Id);
        Assert.Equal(run.Id, installed.RunId);
        Assert.Null(called.HandedOverUtc);
        Assert.Null(called.BootReturns);
        Assert.Equal(2, (await StoredAsync(run.Id)).BootReturns);
    }

    // A report whose answer was lost is sent again, and an older one may arrive late.
    [Fact]
    public async Task AReportSentAgainOrLateNeverLowersTheCount()
    {
        (DeployingMachine machine, AgentRun run) = await HandedOverAsync("LINUX-61");
        using DeployingMachine owned = machine;
        (_, string token) = await ReturnAsync(machine);

        foreach (int? returns in (int?[])[2, 2, 1, null])
        {
            (await machine.Agent.RunReportAsync(machine.Id, token, run.Id, Returned(returns))).EnsureSuccessStatusCode();
        }

        Assert.Equal(2, (await StoredAsync(run.Id)).BootReturns);
        Assert.Equal(2, (await ShownAsync(machine.Id)).BootReturns);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(DeploymentLimits.MaxBootReturns + 1)]
    public async Task RefusesACountThatCannotBe(int returns)
    {
        (DeployingMachine machine, AgentRun run) = await HandedOverAsync($"LINUX-62-{Math.Abs(returns)}");
        using DeployingMachine owned = machine;
        (_, string token) = await ReturnAsync(machine);

        HttpResponseMessage refused = await machine.Agent.RunReportAsync(machine.Id, token, run.Id, Returned(returns));

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(0, (await StoredAsync(run.Id)).BootReturns);
    }

    // The boot agent gives the run up with its own sentence, in the phase of the installed Linux and with no step.
    [Fact]
    public async Task TheBootAgentFailsARunTheInstalledLinuxNeverTookOver()
    {
        const string Error = "The machine started from the network 3 times after the run was handed over.";
        (DeployingMachine machine, AgentRun run) = await HandedOverAsync("LINUX-63");
        using DeployingMachine owned = machine;
        (_, string token) = await ReturnAsync(machine);
        AgentRunReport failed = new(DeploymentState.Failed, SequencePhase.Linux, [], null, 0, RunActivity.Preparing, Error, BootReturns: 3);

        (await machine.Agent.RunReportAsync(machine.Id, token, run.Id, failed)).EnsureSuccessStatusCode();

        DeploymentView view = await (await application.AdministratorAsync()).RunAsync(run.Id);
        Assert.Equal((DeploymentState.Failed, Error, SequencePhase.Linux), (view.Summary.State, view.Summary.Error, view.Summary.Phase));
        Assert.Equal([StepState.Done, StepState.Done, StepState.Pending], view.Steps.Select(step => step.State));
        MachineSummary shown = await ShownAsync(machine.Id);
        Assert.Equal(MachineState.Failed, shown.State);
        Assert.Null(shown.HandedOverUtc);
        Assert.Null(shown.BootReturns);

        // The token on the seed is worth nothing from then on.
        AgentRegistrationResult after = await RegisteredMachine.ReadAsync<AgentRegistrationResult>(
            await machine.Agent.RegisterAsync(machine.Registration with { RunToken = machine.RunToken }));
        Assert.Null(after.RunId);
    }

    // An agent that does not look at the disks, or could not read them, registers as after any start.
    [Fact]
    public async Task ARegistrationWithoutTheTokenEndsTheRunAndSaysWhatTheServerKnows()
    {
        (DeployingMachine machine, AgentRun run) = await HandedOverAsync("LINUX-64");
        using DeployingMachine owned = machine;

        AgentRegistrationResult registered = await machine.RegisterAgainAsync();

        DeploymentSummary failed = (await (await application.AdministratorAsync()).RunAsync(run.Id)).Summary;
        Assert.Equal(MachineState.Pending, registered.State);
        Assert.Null(registered.RunId);
        Assert.Equal((DeploymentState.Failed, WithoutToken), (failed.State, failed.Error));
    }

    [Fact]
    public async Task AStartOverChosenAtTheMachineEndsTheRunAndSaysSo()
    {
        (DeployingMachine machine, AgentRun run) = await HandedOverAsync("LINUX-65");
        using DeployingMachine owned = machine;

        // The installed Linux has the run by now, which is when the boot agent offers the key.
        await RegisteredMachine.ReadAsync<AgentRegistrationResult>(await machine.Agent.RegisterAsync(
            machine.Registration with { RunToken = machine.RunToken, Environment = AgentEnvironment.Linux }));
        AgentRegistrationResult registered = await RegisteredMachine.ReadAsync<AgentRegistrationResult>(
            await machine.Agent.RegisterAsync(machine.Registration with { StartsOver = true }));

        DeploymentSummary failed = (await (await application.AdministratorAsync()).RunAsync(run.Id)).Summary;
        Assert.Equal((MachineState.Pending, AgentEnvironment.LinuxBoot), (registered.State, (await ShownAsync(machine.Id)).AgentEnvironment));
        Assert.Equal((DeploymentState.Failed, StartedOver), (failed.State, failed.Error));
    }

    // Nothing was handed over, so nobody was offered a key, whatever the registration claims.
    [Fact]
    public async Task ARunStillInItsBootEnvironmentKeepsTheSentenceOfARestartWithoutTheToken()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        Image image = await application.SeedRawImageAsync(RandomNumberGenerator.GetBytes(4096), name: $"noble {Guid.NewGuid():N}");
        SequenceView sequence = await administrator.CreatedSequenceAsync(SequenceRequests.LinuxWithInstalledSteps(image.Id));
        using DeployingMachine machine = await DeployingMachine.RegisterAsync(application, DeployingMachine.LinuxBoot());
        (await administrator.PostAsync($"/api/machines/{machine.Id}/approve")).EnsureSuccessStatusCode();
        await machine.NextAsync();
        await administrator.AssignedAsync(machine.Id, sequence.Id, "LINUX-66");
        AgentRun run = Assert.IsType<AgentRun>((await machine.NextAsync()).Run);
        await machine.ReportOkAsync(run.Id, Running(Step(run.Sequence.Steps[0], StepState.Running)) with { Phase = SequencePhase.LinuxBoot });

        await RegisteredMachine.ReadAsync<AgentRegistrationResult>(await machine.Agent.RegisterAsync(machine.Registration with { StartsOver = true }));

        Assert.Equal(WithoutToken, (await administrator.RunAsync(run.Id)).Summary.Error);
    }
}
