// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Server.Data;
using DDT.Server.Deployments;
using DDT.Server.Machines;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DDT.Server.Tests;

// The server counts a start from the network once, however often its agent asks and whatever asks beside it. The
// agent names the start with an id it makes once, and asks again when an answer got lost.
public sealed class NetworkDetourStartTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private Task<Deployment> StoredAsync(Guid runId) =>
        application.QueryAsync(database => database.Deployments.AsNoTracking().SingleAsync(d => d.Id == runId, Cancellation));

    private Task<Machine> StoredMachineAsync(Guid machineId) =>
        application.QueryAsync(database => database.Machines.AsNoTracking().SingleAsync(m => m.Id == machineId, Cancellation));

    // The starts the run's log names, in the order they were counted.
    private async Task<List<string>> LinesAsync(Guid runId)
    {
        const string Counted = "This is start ";
        List<string> lines = await application.QueryAsync(database => database.MachineLogLines
            .AsNoTracking()
            .Where(line => line.DeploymentId == runId && line.Message.StartsWith("The machine started from the network"))
            .OrderBy(line => line.Id)
            .Select(line => line.Message)
            .ToListAsync(Cancellation));

        return [.. lines.Select(line => line.Substring(line.IndexOf(Counted, StringComparison.Ordinal) + Counted.Length, 1))];
    }

    private Task<int> AuditedAsync(Guid runId)
    {
        string subject = runId.ToString("D");

        return application.QueryAsync(database => database.AuditEvents.CountAsync(e => e.SubjectId == subject && e.Action == AuditActions.DeploymentDetoured, Cancellation));
    }

    private static NormalisedRegistration Question(DeployingMachine machine, Guid runId, Guid? start)
    {
        Assert.True(RegistrationValidator.TryNormalise(machine.Registration with { HandedOver = new AgentHandedOverRun(runId, start) }, out NormalisedRegistration? question, out _));

        return Assert.IsType<NormalisedRegistration>(question);
    }

    private async Task<(DeployingMachine Machine, AgentRun Run)> InstalledAsync(string name)
    {
        (DeployingMachine machine, AgentRun run) = await HandedOverLinuxRun.StartAsync(application, name);
        await HandedOverLinuxRun.InstalledAsync(machine);

        return (machine, run);
    }

    // The agent tries five times. Counted each, one start whose answers were lost would end a healthy run.
    [Fact]
    public async Task AStartThatAsksAgainIsCountedOnceAndWritesOneLine()
    {
        (DeployingMachine machine, AgentRun run) = await InstalledAsync("LINUX-80");
        using DeployingMachine owned = machine;
        Guid start = Guid.NewGuid();

        for (int attempt = 0; attempt < NetworkDetours.Max + 1; attempt++)
        {
            Assert.Equal(new AgentInstalledRun(run.Id, 1, 5), (await HandedOverLinuxRun.AskAsync(machine, run.Id, start)).InstalledRun);
        }

        Deployment stored = await StoredAsync(run.Id);
        Assert.Equal((DeploymentState.Running, 1, start), (stored.State, stored.Detours, stored.DetourStart));
        Assert.Equal(["1"], await LinesAsync(run.Id));
        Assert.Equal(1, await AuditedAsync(run.Id));

        // The next start is another one.
        Assert.Equal(2, (await HandedOverLinuxRun.AskAsync(machine, run.Id, Guid.NewGuid())).InstalledRun?.Detours);
        Assert.Equal(["1", "2"], await LinesAsync(run.Id));
    }

    [Fact]
    public async Task ACallFromTheInstalledLinuxForgetsTheStartWithTheCount()
    {
        (DeployingMachine machine, AgentRun run) = await InstalledAsync("LINUX-81");
        using DeployingMachine owned = machine;
        Guid start = Guid.NewGuid();
        Assert.Equal(1, (await HandedOverLinuxRun.AskAsync(machine, run.Id, start)).InstalledRun?.Detours);

        await HandedOverLinuxRun.InstalledAsync(machine);

        Deployment stored = await StoredAsync(run.Id);
        Assert.Equal((0, null), (stored.Detours, stored.DetourStart));
        Assert.Equal(1, (await HandedOverLinuxRun.AskAsync(machine, run.Id, start)).InstalledRun?.Detours);
        Assert.Equal(["1", "1"], await LinesAsync(run.Id));
    }

    [Fact]
    public async Task QuestionsOfOneStartInParallelCountOnce()
    {
        (DeployingMachine machine, AgentRun run) = await InstalledAsync("LINUX-82");
        using DeployingMachine owned = machine;
        Guid start = Guid.NewGuid();

        AgentRegistrationResult[] answers = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => HandedOverLinuxRun.AskAsync(machine, run.Id, start)));

        Assert.All(answers, answer => Assert.Equal(new AgentInstalledRun(run.Id, 1, 5), answer.InstalledRun));
        Assert.Equal(1, (await StoredAsync(run.Id)).Detours);
        Assert.Equal(["1"], await LinesAsync(run.Id));
        Assert.Equal(1, await AuditedAsync(run.Id));
    }

    // Three, because a registration decides again at most twice.
    [Fact]
    public async Task StartsInParallelAreEachCountedAndEachWriteOneLine()
    {
        (DeployingMachine machine, AgentRun run) = await InstalledAsync("LINUX-83");
        using DeployingMachine owned = machine;

        AgentRegistrationResult[] answers = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => HandedOverLinuxRun.AskAsync(machine, run.Id, Guid.NewGuid())));

        Assert.Equal([1, 2, 3], answers.Select(answer => answer.InstalledRun?.Detours).Order());
        Assert.Equal(3, (await StoredAsync(run.Id)).Detours);
        Assert.Equal(["1", "2", "3"], await LinesAsync(run.Id));
        Assert.Equal(3, await AuditedAsync(run.Id));
    }

    // Both read the count before either saves. Without the check on the count, each would add a line for the same start.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OfTwoQuestionsThatReadTheSameCountOneSavesAndTheOtherDecidesAgain(bool namesItsStart)
    {
        (DeployingMachine machine, AgentRun run) = await InstalledAsync(namesItsStart ? "LINUX-84A" : "LINUX-84B");
        using DeployingMachine owned = machine;
        DateTimeOffset now = application.Services.GetRequiredService<TimeProvider>().GetUtcNow();
        using IServiceScope first = application.Services.CreateScope();
        using IServiceScope second = application.Services.CreateScope();
        MachineArrivals late = second.ServiceProvider.GetRequiredService<MachineArrivals>();
        NormalisedRegistration other = Question(machine, run.Id, namesItsStart ? Guid.NewGuid() : null);

        Arrival counted = await first.ServiceProvider.GetRequiredService<MachineArrivals>()
            .RecordAsync(Question(machine, run.Id, namesItsStart ? Guid.NewGuid() : null), IPAddress.Loopback, now, Cancellation);
        Arrival lost = await late.RecordAsync(other, IPAddress.Loopback, now, Cancellation);
        Assert.NotNull(counted.Detour);
        Assert.NotNull(lost.Detour);

        await first.ServiceProvider.GetRequiredService<DdtDbContext>().SaveChangesAsync(Cancellation);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.ServiceProvider.GetRequiredService<DdtDbContext>().SaveChangesAsync(Cancellation));
        Assert.Equal(["1"], await LinesAsync(run.Id));

        late.Forget();
        Arrival again = await late.RecordAsync(other, IPAddress.Loopback, now, Cancellation);
        await second.ServiceProvider.GetRequiredService<DdtDbContext>().SaveChangesAsync(Cancellation);

        Assert.True(again.GoesOnInstalled);
        Assert.Equal(2, (await StoredAsync(run.Id)).Detours);
        Assert.Equal(["1", "2"], await LinesAsync(run.Id));
    }

    // The question read the run before the stop was saved. The stop wins, and the question is decided again.
    [Fact]
    public async Task AQuestionThatRacesAnOperatorsStopLosesAndIsThenARegistrationAsAnyOther()
    {
        (DeployingMachine machine, AgentRun run) = await InstalledAsync("LINUX-85");
        using DeployingMachine owned = machine;
        DateTimeOffset now = application.Services.GetRequiredService<TimeProvider>().GetUtcNow();
        using IServiceScope scope = application.Services.CreateScope();
        MachineArrivals arrivals = scope.ServiceProvider.GetRequiredService<MachineArrivals>();
        NormalisedRegistration question = Question(machine, run.Id, Guid.NewGuid());

        Arrival asked = await arrivals.RecordAsync(question, IPAddress.Loopback, now, Cancellation);
        Assert.True(asked.GoesOnInstalled);
        SignedInClient administrator = await application.AdministratorAsync();
        (await administrator.EndCurrentAsync(machine.Id)).EnsureSuccessStatusCode();
        string stopped = Assert.IsType<string>((await administrator.RunAsync(run.Id)).Summary.Error);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => scope.ServiceProvider.GetRequiredService<RegistrationPublisher>().PublishAsync(Assert.IsType<Machine>(asked.Machine), asked, Cancellation));
        arrivals.Forget();
        Arrival again = await arrivals.RecordAsync(question, IPAddress.Loopback, now, Cancellation);
        await scope.ServiceProvider.GetRequiredService<DdtDbContext>().SaveChangesAsync(Cancellation);

        // Nothing of the question stays: no count, no line, and the run's error is the operator's.
        Assert.False(again.GoesOnInstalled);
        Deployment stored = await StoredAsync(run.Id);
        Assert.Equal((DeploymentState.Failed, stopped, 0), (stored.State, stored.Error, stored.Detours));
        Assert.Empty(await LinesAsync(run.Id));
        Assert.Equal(0, await AuditedAsync(run.Id));
        Assert.Equal(MachineState.Pending, (await StoredMachineAsync(machine.Id)).State);
    }

    // The other way round the stop read the run before the question was counted, and is asked to be given again.
    [Fact]
    public async Task AStopThatRacesACountedQuestionIsRefusedOnceAndWorksTheNextTime()
    {
        (DeployingMachine machine, AgentRun run) = await InstalledAsync("LINUX-86");
        using DeployingMachine owned = machine;
        using IServiceScope scope = application.Services.CreateScope();
        DdtDbContext database = scope.ServiceProvider.GetRequiredService<DdtDbContext>();
        Machine stopping = await database.Machines.SingleAsync(m => m.Id == machine.Id, Cancellation);
        DeploymentDecision decision = await scope.ServiceProvider.GetRequiredService<RunTermination>()
            .EndCurrentAsync(stopping, new Actor(null, "operator", null), Cancellation);

        Assert.Equal(1, (await HandedOverLinuxRun.AskAsync(machine, run.Id, Guid.NewGuid())).InstalledRun?.Detours);

        Assert.False(await scope.ServiceProvider.GetRequiredService<MachineChangePublisher>()
            .SaveAsync(stopping, Assert.IsType<Deployment>(decision.Deployment), DeploymentState.Running, typeof(NetworkDetourStartTests), Cancellation));
        Assert.Equal(DeploymentState.Running, (await StoredAsync(run.Id)).State);
        (await (await application.AdministratorAsync()).EndCurrentAsync(machine.Id)).EnsureSuccessStatusCode();
        Assert.Equal(DeploymentState.Failed, (await StoredAsync(run.Id)).State);
    }

    // A resume token is from before the hand-over. The installed Linux has the run, so the question is one as any other.
    [Fact]
    public async Task AQuestionWithAValidResumeTokenAboutARunInTheInstalledLinuxChangesNothingEither()
    {
        (DeployingMachine machine, AgentRun run) = await InstalledAsync("LINUX-87");
        using DeployingMachine owned = machine;
        Machine before = await StoredMachineAsync(machine.Id);

        AgentRegistrationResult answer = await RegisteredMachine.ReadAsync<AgentRegistrationResult>(await machine.Agent.RegisterAsync(
            machine.Registration with { ResumeToken = machine.ResumeToken, HandedOver = new AgentHandedOverRun(run.Id, Guid.NewGuid()) }));

        Assert.Equal(new AgentInstalledRun(run.Id, 1, 5), answer.InstalledRun);
        Assert.Null(answer.Token);
        Assert.Null(answer.ResumeToken);
        Assert.Null(answer.RunId);
        Assert.Null(answer.RunToken);
        Machine after = await StoredMachineAsync(machine.Id);
        Assert.Equal(
            (AgentEnvironment.Linux, before.TokenGeneration, before.State, before.LastSeenUtc),
            (after.AgentEnvironment, after.TokenGeneration, after.State, after.LastSeenUtc));
        Assert.Equal(1, (await StoredAsync(run.Id)).Detours);
    }

    // At the limit the run is over, so the resume token has nothing to continue and the machine starts over.
    [Fact]
    public async Task TheFifthStartEndsTheRunAlsoForAQuestionWithAValidResumeToken()
    {
        (DeployingMachine machine, AgentRun run) = await InstalledAsync("LINUX-88");
        using DeployingMachine owned = machine;
        AgentRegistration question = machine.Registration with { ResumeToken = machine.ResumeToken };

        for (int detour = 1; detour < NetworkDetours.Max; detour++)
        {
            Assert.Equal(detour, (await HandedOverLinuxRun.AskAsync(machine, run.Id, Guid.NewGuid())).InstalledRun?.Detours);
        }

        AgentRegistrationResult fifth = await RegisteredMachine.ReadAsync<AgentRegistrationResult>(
            await machine.Agent.RegisterAsync(question with { HandedOver = new AgentHandedOverRun(run.Id, Guid.NewGuid()) }));

        Assert.Null(fifth.InstalledRun);
        Assert.Null(fifth.RunId);
        Assert.Null(fifth.RunToken);
        Assert.Equal(MachineState.Pending, fifth.State);
        Assert.Equal((DeploymentState.Failed, NetworkDetours.TooMany), ((await StoredAsync(run.Id)).State, (await StoredAsync(run.Id)).Error));
    }

    // A machine that came from Windows PE for a Linux sequence, whose disk still holds the seed of an earlier run.
    [Fact]
    public async Task AStaleSeedOnAMachineThatCarriesItsResumeTokenLeavesItARegistrationThatContinues()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, administrator, [DeployingMachine.Disk(0)]);
        Machine before = await StoredMachineAsync(machine.Id);

        AgentRegistrationResult answer = await RegisteredMachine.ReadAsync<AgentRegistrationResult>(await machine.Agent.RegisterAsync(machine.Registration with
        {
            Environment = AgentEnvironment.LinuxBoot,
            ResumeToken = machine.ResumeToken,
            HandedOver = new AgentHandedOverRun(Guid.CreateVersion7(), Guid.NewGuid()),
        }));

        // The machine is not started over: it stays approved, its tokens stay good, and it is where it registered from.
        Assert.Null(answer.InstalledRun);
        Assert.Null(answer.RunId);
        Assert.Equal(MachineState.Approved, answer.State);
        Assert.NotNull(answer.Token);
        Assert.NotNull(answer.ResumeToken);
        Machine after = await StoredMachineAsync(machine.Id);
        Assert.Equal((before.TokenGeneration, AgentEnvironment.LinuxBoot), (after.TokenGeneration, after.AgentEnvironment));
    }
}
