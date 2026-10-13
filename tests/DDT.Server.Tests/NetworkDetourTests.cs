// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Threading.Channels;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Contracts.Sequences;
using DDT.Server.Deployments;
using DDT.Server.Live;
using DDT.Server.Machines;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DDT.Server.Tests.TestReports;

namespace DDT.Server.Tests;

// A machine starts the Linux boot environment while its run goes on in the installed Linux. Its agent has no token and
// asks before it registers: the server answers that the run goes on and changes nothing, up to five times in a row.
public sealed class NetworkDetourTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    private const string WithoutToken = "The machine started again during the run, without the run's token, so the run could not continue.";

    private const string TooMany =
        "The machine started from the network 5 times in a row while the run went on in the installed Linux, with no call from the " +
        "installed Linux in between. After each of the first 4 starts the agent of the boot environment sent it back to its disk, " +
        "and it came from the network again, so the installed Linux is no longer reached. Check the firmware's boot order and " +
        "whether the machine starts from its disk.";

    private static string Line(int detours) =>
        "The machine started from the network while this run goes on in the installed Linux, and the agent of the boot environment " +
        $"sends it back to its disk. This is start {detours} of 5 in a row without a call from the installed Linux in between. At start 5, " +
        "the server ends the run.";

    // The boot agent's question: no token at all, and the run the seed's state names, if it names one.
    private static async Task<AgentRegistrationResult> AskAsync(DeployingMachine machine, Guid? runId) =>
        await RegisteredMachine.ReadAsync<AgentRegistrationResult>(await machine.Agent.RegisterAsync(machine.Registration with { HandedOver = new AgentHandedOverRun(runId) }));

    private Task<Deployment> StoredAsync(Guid runId) =>
        application.QueryAsync(database => database.Deployments.AsNoTracking().SingleAsync(d => d.Id == runId, TestContext.Current.CancellationToken));

    private Task<Machine> StoredMachineAsync(Guid machineId) =>
        application.QueryAsync(database => database.Machines.AsNoTracking().SingleAsync(m => m.Id == machineId, TestContext.Current.CancellationToken));

    private async Task<IReadOnlyList<MachineLogEntry>> RunLogAsync(Guid machineId, Guid runId) =>
        (await RegisteredMachine.ReadAsync<MachineLogPage>(
            await (await application.AdministratorAsync()).GetAsync($"/api/machines/{machineId}/log?deploymentId={runId}"))).Lines;

    [Fact]
    public async Task ARunTheInstalledLinuxHasGoesOnAndNothingOfTheRegistrationIsTaken()
    {
        (DeployingMachine machine, AgentRun run) = await HandedOverLinuxRun.StartAsync(application, "LINUX-70");
        using DeployingMachine owned = machine;
        AgentRegistrationResult installed = await HandedOverLinuxRun.InstalledAsync(machine);
        string session = Assert.IsType<string>(installed.Token);
        Machine before = await StoredMachineAsync(machine.Id);
        SignedInClient administrator = await application.AdministratorAsync();
        await using LiveListener watcher = await LiveListener.StartAsync(application, administrator);
        ChannelReader<MachineLogAppendedEvent> logs = watcher.Listen<MachineLogAppendedEvent>(LiveEvents.MachineLogAppended);
        await watcher.WatchAsync(machine.Id);

        AgentRegistrationResult answer = await AskAsync(machine, run.Id);

        // No token goes to a registration anyone could have sent, and the answer names the run and the count.
        Assert.Equal(new AgentInstalledRun(run.Id, 1, 5), answer.InstalledRun);
        Assert.Equal((machine.Id, MachineState.Deploying), (answer.MachineId, answer.State));
        Assert.Null(answer.Token);
        Assert.Null(answer.ResumeToken);
        Assert.Null(answer.RunId);
        Assert.Null(answer.RunToken);

        // The machine is as the installed Linux left it, and its tokens still count.
        Machine after = await StoredMachineAsync(machine.Id);
        Assert.Equal(
            (before.TokenGeneration, before.State, AgentEnvironment.Linux, before.LastSeenUtc, before.ApprovedUtc),
            (after.TokenGeneration, after.State, after.AgentEnvironment, after.LastSeenUtc, after.ApprovedUtc));
        Deployment stored = await StoredAsync(run.Id);
        Assert.Equal((DeploymentState.Running, 1, null), (stored.State, stored.Detours, stored.Error));
        (await machine.Agent.RunReportAsync(machine.Id, session, run.Id, Running(Step(run.Sequence.Steps[2], StepState.Running)) with { Phase = SequencePhase.Linux }))
            .EnsureSuccessStatusCode();

        // One line in the run's log says that the machine came through the network, and the page hears of it.
        MachineLogEntry said = Assert.Single(await RunLogAsync(machine.Id, run.Id), line => line.Message.StartsWith("The machine started from the network", StringComparison.Ordinal));
        Assert.Equal((Line(1), AgentLogLevel.Warning, (Guid?)run.Id), (said.Message, said.Level, said.DeploymentId));
        Assert.Equal(said.Id, (await LiveListener.NextAsync(logs, appended => appended.LastLineId == said.Id)).LastLineId);
    }

    // The script wiped the token off the seed, and power was lost before the installed agent's first call.
    [Fact]
    public async Task ARunHandedOverGoesOnAlsoBeforeTheInstalledLinuxHasCalled()
    {
        (DeployingMachine machine, AgentRun run) = await HandedOverLinuxRun.StartAsync(application, "LINUX-71");
        using DeployingMachine owned = machine;
        DateTimeOffset handedOver = (await StoredAsync(run.Id)).UpdatedUtc;

        AgentRegistrationResult answer = await AskAsync(machine, run.Id);

        Assert.Equal(new AgentInstalledRun(run.Id, 1, 5), answer.InstalledRun);
        Deployment stored = await StoredAsync(run.Id);
        Assert.Equal((DeploymentState.Running, handedOver, 0), (stored.State, stored.UpdatedUtc, stored.BootReturns));
        Assert.Equal(run.Id, (await HandedOverLinuxRun.InstalledAsync(machine)).RunId);
    }

    // A state on the seed that cannot be read names no run. The server then answers for the run the machine has.
    [Fact]
    public async Task AQuestionThatNamesNoRunIsAnsweredForTheMachinesRun()
    {
        (DeployingMachine machine, AgentRun run) = await HandedOverLinuxRun.StartAsync(application, "LINUX-72");
        using DeployingMachine owned = machine;
        await HandedOverLinuxRun.InstalledAsync(machine);

        AgentRegistrationResult answer = await AskAsync(machine, null);

        Assert.Equal(new AgentInstalledRun(run.Id, 1, 5), answer.InstalledRun);
        Assert.Equal(DeploymentState.Running, (await StoredAsync(run.Id)).State);
    }

    // What an operator does who wants a machine back whose installed Linux no longer works.
    [Fact]
    public async Task AfterAStopOnTheWebTheQuestionIsARegistrationAsAnyOtherAndTheMachineIsBack()
    {
        (DeployingMachine machine, AgentRun run) = await HandedOverLinuxRun.StartAsync(application, "LINUX-73");
        using DeployingMachine owned = machine;
        await HandedOverLinuxRun.InstalledAsync(machine);
        Assert.NotNull((await AskAsync(machine, run.Id)).InstalledRun);
        SignedInClient administrator = await application.AdministratorAsync();
        (await administrator.EndCurrentAsync(machine.Id)).EnsureSuccessStatusCode();
        string stopped = Assert.IsType<string>((await administrator.RunAsync(run.Id)).Summary.Error);

        AgentRegistrationResult answer = await AskAsync(machine, run.Id);

        // The machine starts over, as after any start from the network, and can be approved and given a run again.
        Assert.Null(answer.InstalledRun);
        Assert.Null(answer.RunId);
        Assert.Equal(MachineState.Pending, answer.State);
        Assert.NotNull(answer.Token);
        Assert.NotNull(answer.ResumeToken);
        Assert.Equal((DeploymentState.Failed, stopped), ((await StoredAsync(run.Id)).State, (await StoredAsync(run.Id)).Error));
        Assert.Equal(AgentEnvironment.LinuxBoot, (await StoredMachineAsync(machine.Id)).AgentEnvironment);
        (await administrator.PostAsync($"/api/machines/{machine.Id}/approve")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task AQuestionAboutAnotherRunThanTheMachinesIsARegistrationAsAnyOther()
    {
        (DeployingMachine machine, AgentRun run) = await HandedOverLinuxRun.StartAsync(application, "LINUX-74");
        using DeployingMachine owned = machine;
        await HandedOverLinuxRun.InstalledAsync(machine);

        AgentRegistrationResult answer = await AskAsync(machine, Guid.CreateVersion7());

        Deployment stored = await StoredAsync(run.Id);
        Assert.Null(answer.InstalledRun);
        Assert.Equal(MachineState.Pending, answer.State);
        Assert.Equal((DeploymentState.Failed, WithoutToken, 0), (stored.State, stored.Error, stored.Detours));
    }

    [Fact]
    public async Task AQuestionFromAnotherMachineIsThatMachinesRegistrationAndLeavesTheRunAlone()
    {
        (DeployingMachine machine, AgentRun run) = await HandedOverLinuxRun.StartAsync(application, "LINUX-75");
        using DeployingMachine owned = machine;
        await HandedOverLinuxRun.InstalledAsync(machine);
        using DeployingMachine other = await DeployingMachine.RegisterAsync(application, DeployingMachine.LinuxBoot());

        AgentRegistrationResult answer = await AskAsync(other, run.Id);

        Deployment stored = await StoredAsync(run.Id);
        Assert.Null(answer.InstalledRun);
        Assert.Equal((other.Id, MachineState.Pending), (answer.MachineId, answer.State));
        Assert.Equal((DeploymentState.Running, 0), (stored.State, stored.Detours));
    }

    // Nothing was handed over yet, so no installed Linux can have the run, whatever the question says.
    [Fact]
    public async Task ARunStillInItsBootEnvironmentIsNotKeptByAQuestion()
    {
        (DeployingMachine machine, AgentRun run) = await HandedOverLinuxRun.AssignedAsync(application, "LINUX-76");
        using DeployingMachine owned = machine;
        await machine.ReportOkAsync(run.Id, Running(Step(run.Sequence.Steps[0], StepState.Running)) with { Phase = SequencePhase.LinuxBoot });

        AgentRegistrationResult answer = await AskAsync(machine, run.Id);

        Assert.Null(answer.InstalledRun);
        Assert.Equal((DeploymentState.Failed, WithoutToken), ((await StoredAsync(run.Id)).State, (await StoredAsync(run.Id)).Error));
    }

    // Windows PE's agent sends no such question. Whoever sends one from there gets a registration as any other.
    [Theory]
    [InlineData(AgentEnvironment.WindowsPE, false)]
    [InlineData(AgentEnvironment.LinuxBoot, true)]
    public async Task OnlyTheLinuxBootEnvironmentAsksAndNotWhenItStartsOverOnPurpose(AgentEnvironment environment, bool startsOver)
    {
        (DeployingMachine machine, AgentRun run) = await HandedOverLinuxRun.StartAsync(application, startsOver ? "LINUX-77B" : "LINUX-77A");
        using DeployingMachine owned = machine;
        await HandedOverLinuxRun.InstalledAsync(machine);

        AgentRegistrationResult answer = await RegisteredMachine.ReadAsync<AgentRegistrationResult>(await machine.Agent.RegisterAsync(
            machine.Registration with { Environment = environment, HandedOver = new AgentHandedOverRun(run.Id), StartsOver = startsOver ? true : null }));

        Assert.Null(answer.InstalledRun);
        Assert.Equal(MachineState.Pending, answer.State);
        Assert.Equal(DeploymentState.Failed, (await StoredAsync(run.Id)).State);
    }

    // A firmware that drops the disk's boot entry, or a disk that no longer starts: the machine comes back for ever.
    [Fact]
    public async Task TheFifthStartFromTheNetworkInARowEndsTheRunAndIsARegistrationAsAnyOther()
    {
        (DeployingMachine machine, AgentRun run) = await HandedOverLinuxRun.StartAsync(application, "LINUX-78");
        using DeployingMachine owned = machine;
        await HandedOverLinuxRun.InstalledAsync(machine);

        for (int detour = 1; detour < NetworkDetours.Max; detour++)
        {
            Assert.Equal(new AgentInstalledRun(run.Id, detour, 5), (await AskAsync(machine, run.Id)).InstalledRun);
        }

        AgentRegistrationResult fifth = await AskAsync(machine, run.Id);

        Assert.Null(fifth.InstalledRun);
        Assert.Null(fifth.RunId);
        Assert.Equal(MachineState.Pending, fifth.State);
        Assert.NotNull(fifth.Token);
        DeploymentView view = await (await application.AdministratorAsync()).RunAsync(run.Id);
        Assert.Equal((DeploymentState.Failed, TooMany), (view.Summary.State, view.Summary.Error));
        Assert.Equal(
            [Line(1), Line(2), Line(3), Line(4)],
            (await RunLogAsync(machine.Id, run.Id)).Select(line => line.Message).Where(message => message.StartsWith("The machine started from the network", StringComparison.Ordinal)));

        // The installed Linux, should it start after all, has nothing to go on with.
        HttpResponseMessage late = await machine.Agent.RegisterAsync(machine.Registration with { RunToken = machine.RunToken, Environment = AgentEnvironment.Linux });
        Assert.Equal(System.Net.HttpStatusCode.Conflict, late.StatusCode);
    }

    // Where the firmware starts the network first, every restart of the installed Linux comes through the boot agent.
    [Fact]
    public async Task ACallFromTheInstalledLinuxInBetweenStartsTheCountAnew()
    {
        (DeployingMachine machine, AgentRun run) = await HandedOverLinuxRun.StartAsync(application, "LINUX-79");
        using DeployingMachine owned = machine;

        for (int restart = 0; restart < 3; restart++)
        {
            await HandedOverLinuxRun.InstalledAsync(machine);
            Assert.Equal(0, (await StoredAsync(run.Id)).Detours);

            for (int detour = 1; detour < NetworkDetours.Max; detour++)
            {
                Assert.Equal(detour, (await AskAsync(machine, run.Id)).InstalledRun?.Detours);
            }
        }

        Assert.Equal(DeploymentState.Running, (await StoredAsync(run.Id)).State);
    }
}
