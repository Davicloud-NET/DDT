// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Security.Cryptography;
using System.Threading.Channels;
using DDT.Contracts.Agents;
using DDT.Contracts.Machines;
using DDT.Contracts.Sequences;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Images;
using DDT.Server.Live;
using DDT.Server.Machines;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DDT.Server.Tests;

// What a machine tells the server before it restarts into the other boot environment. Nothing here makes a run.
public sealed class EnvironmentSwitchTests(BothEnvironmentsApplication application) : IClassFixture<BothEnvironmentsApplication>
{
    private const string Requested = "machine.environment-requested";

    private async Task<SequenceView> LinuxAsync()
    {
        Image image = await application.SeedRawImageAsync(RandomNumberGenerator.GetBytes(4096), name: $"noble {Guid.NewGuid():N}");

        return await (await application.AdministratorAsync()).CreatedSequenceAsync(SequenceRequests.Linux(image.Id));
    }

    private async Task<SequenceView> WindowsAsync()
    {
        Image image = await application.SeedImageAsync(RandomNumberGenerator.GetBytes(4096));

        return await (await application.AdministratorAsync()).CreatedSequenceAsync(SequenceRequests.Minimal(image.Id));
    }

    // A machine in Windows PE that an operator signed in at.
    private async Task<(DeployingMachine Machine, string OperatorName)> SignedInAsync(int? sequenceVersion = null)
    {
        string operatorName = await application.CreateUserAsync(DdtRoleNames.Operator);

        return (await DeployingMachine.SignedInAsync(application, operatorName, [DeployingMachine.Disk(0)], sequenceVersion: sequenceVersion), operatorName);
    }

    private static Task<HttpResponseMessage> AskAsync(DeployingMachine machine, AgentEnvironment environment, Guid? sequenceId = null) =>
        machine.Agent.EnvironmentAsync(machine.Id, machine.Token, new AgentEnvironmentRequest(environment, sequenceId));

    private Task<List<AuditEvent>> AuditAsync(Guid machineId)
    {
        string subject = machineId.ToString("D");

        return application.QueryAsync(database => database.AuditEvents
            .AsNoTracking()
            .Where(e => e.SubjectId == subject && e.Action == Requested)
            .ToListAsync(TestContext.Current.CancellationToken));
    }

    private async Task AssertNothingKeptAsync(Guid machineId)
    {
        Machine stored = await application.MachineAsync(machineId);

        Assert.Null(stored.NextEnvironment);
        Assert.Null(stored.NextEnvironmentUtc);
        Assert.Null(stored.PendingSequenceId);
        Assert.Empty(await AuditAsync(machineId));
    }

    [Fact]
    public async Task KeepsWhereTheMachineRestartsWhenItSaidSoAndWhatWasPickedForIt()
    {
        (DeployingMachine machine, string operatorName) = await SignedInAsync();
        using DeployingMachine owned = machine;
        SequenceView linux = await LinuxAsync();
        Machine before = await application.MachineAsync(machine.Id);

        HttpResponseMessage answer = await AskAsync(machine, AgentEnvironment.LinuxBoot, linux.Id);

        Assert.Equal(HttpStatusCode.NoContent, answer.StatusCode);

        Machine stored = await application.MachineAsync(machine.Id);
        Assert.Equal(AgentEnvironment.LinuxBoot, stored.NextEnvironment);
        Assert.Equal(application.Clock.GetUtcNow(), stored.NextEnvironmentUtc);
        Assert.Equal(linux.Id, stored.PendingSequenceId);

        // It grants nothing: no run, and the machine is as approved and signed in as it was.
        Assert.Equal(
            (before.State, before.TokenGeneration, before.SignedInUserName, before.AgentEnvironment),
            (stored.State, stored.TokenGeneration, stored.SignedInUserName, stored.AgentEnvironment));
        Assert.Null(stored.ActiveDeploymentId);
        Assert.False(await application.QueryAsync(database => database.Deployments.AnyAsync(d => d.MachineId == machine.Id, TestContext.Current.CancellationToken)));

        AuditEvent audit = Assert.Single(await AuditAsync(machine.Id));
        Assert.Equal((operatorName, machine.Id, machine.Agent.RemoteAddress), (audit.ActorName, audit.ActorMachineId, audit.SourceAddress));
        Assert.Equal(
            $"Restarts from Windows PE into the Linux boot environment for {linux.Name}, chosen at the machine. No run exists until it is chosen again there.",
            audit.Detail);
    }

    // The agent restarts for a web assignment too, where nothing was picked at the machine.
    [Fact]
    public async Task ARestartWithoutASequenceIsKeptToo()
    {
        (DeployingMachine machine, _) = await SignedInAsync();
        using DeployingMachine owned = machine;

        Assert.Equal(HttpStatusCode.NoContent, (await AskAsync(machine, AgentEnvironment.LinuxBoot)).StatusCode);

        Machine stored = await application.MachineAsync(machine.Id);
        Assert.Equal(AgentEnvironment.LinuxBoot, stored.NextEnvironment);
        Assert.Null(stored.PendingSequenceId);
        Assert.Equal("Restarts from Windows PE into the Linux boot environment, asked at the machine.", Assert.Single(await AuditAsync(machine.Id)).Detail);
    }

    [Fact]
    public async Task TheMachinesPageLearnsOfTheRestartAtOnce()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        await using LiveListener listener = await LiveListener.StartAsync(application, administrator);
        ChannelReader<MachineSummary> pushes = listener.Listen<MachineSummary>(LiveEvents.MachineChanged);
        (DeployingMachine machine, _) = await SignedInAsync();
        using DeployingMachine owned = machine;

        // A machine row is pushed at most once a second, and the sign-in was pushed a moment ago.
        application.Clock.Advance(LiveNotifier.MachinePushInterval);
        Assert.Equal(HttpStatusCode.NoContent, (await AskAsync(machine, AgentEnvironment.LinuxBoot)).StatusCode);
        application.Clock.Advance(LiveNotifier.MachinePushInterval);

        MachineSummary pushed = await LiveListener.NextAsync(pushes, m => m.Id == machine.Id && m.NextEnvironment is not null);
        IReadOnlyList<MachineSummary> listed = await RegisteredMachine.ReadAsync<IReadOnlyList<MachineSummary>>(await administrator.GetAsync("/api/machines"));

        Assert.Equal((AgentEnvironment.WindowsPE, AgentEnvironment.LinuxBoot), (pushed.AgentEnvironment, pushed.NextEnvironment));
        Assert.Equal(AgentEnvironment.LinuxBoot, Assert.Single(listed, m => m.Id == machine.Id).NextEnvironment);
    }

    [Fact]
    public async Task OnlyAMachineSomeoneWhoMayDeploySignedInAtMaySayIt()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine approvedOnTheWeb = await DeployingMachine.ApprovedAsync(application, administrator, [DeployingMachine.Disk(0)]);
        using DeployingMachine waiting = await DeployingMachine.RegisterAsync(application);
        (DeployingMachine signedIn, _) = await SignedInAsync();
        using DeployingMachine owned = signedIn;

        HttpResponseMessage nobodySignedIn = await AskAsync(approvedOnTheWeb, AgentEnvironment.LinuxBoot);

        Assert.Equal(HttpStatusCode.Forbidden, nobodySignedIn.StatusCode);
        Assert.Equal(
            "Only an operator or administrator signed in at this machine can restart it into another boot environment. Sign in at the machine first.",
            await TestDatabase.TitleAsync(nobodySignedIn));

        // A waiting machine only holds a poll token, and another machine's token is not this machine's.
        Assert.Equal(HttpStatusCode.Forbidden, (await AskAsync(waiting, AgentEnvironment.LinuxBoot)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await signedIn.Agent.EnvironmentAsync(approvedOnTheWeb.Id, signedIn.Token, new AgentEnvironmentRequest(AgentEnvironment.LinuxBoot))).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await signedIn.Agent.EnvironmentAsync(signedIn.Id, "not a token", new AgentEnvironmentRequest(AgentEnvironment.LinuxBoot))).StatusCode);

        await AssertNothingKeptAsync(approvedOnTheWeb.Id);
        await AssertNothingKeptAsync(waiting.Id);
        await AssertNothingKeptAsync(signedIn.Id);
    }

    // A viewer can never sign in at a machine. Someone who did as an operator and is a viewer by now still shows as
    // signed in there, and must not be able to send the machine anywhere either.
    [Fact]
    public async Task AViewerWhoIsStillSignedInAtTheMachineMayNotSayIt()
    {
        (DeployingMachine machine, string operatorName) = await SignedInAsync();
        using DeployingMachine owned = machine;
        SequenceView linux = await LinuxAsync();

        using (IServiceScope scope = application.Services.CreateScope())
        {
            UserManager<DdtUser> users = scope.ServiceProvider.GetRequiredService<UserManager<DdtUser>>();
            DdtUser user = Assert.IsType<DdtUser>(await users.FindByNameAsync(operatorName));

            Assert.True((await users.RemoveFromRoleAsync(user, DdtRoleNames.Operator)).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, DdtRoleNames.Viewer)).Succeeded);
        }

        HttpResponseMessage refused = await AskAsync(machine, AgentEnvironment.LinuxBoot, linux.Id);

        Assert.Equal(operatorName, (await application.MachineAsync(machine.Id)).SignedInUserName);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal(
            "Only an operator or administrator signed in at this machine can restart it into another boot environment. Sign in at the machine first.",
            await TestDatabase.TitleAsync(refused));
        await AssertNothingKeptAsync(machine.Id);
    }

    // A machine with a run does not pick, so it does not restart for a pick either. Its run tells it where to go.
    [Fact]
    public async Task AMachineWithARunMayNotSayIt()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        (DeployingMachine machine, _) = await SignedInAsync();
        using DeployingMachine owned = machine;
        await administrator.AssignedAsync(machine.Id, (await LinuxAsync()).Id, "LINUX-20");

        Assert.Equal(HttpStatusCode.Forbidden, (await AskAsync(machine, AgentEnvironment.LinuxBoot)).StatusCode);
        Assert.Null((await application.MachineAsync(machine.Id)).NextEnvironment);
    }

    // A token from before the machine started over is no longer the machine's.
    [Fact]
    public async Task ATokenFromBeforeTheMachineStartedOverIsRefused()
    {
        (DeployingMachine machine, _) = await SignedInAsync();
        using DeployingMachine owned = machine;
        string before = machine.Token;
        await machine.RegisterAgainAsync();

        HttpResponseMessage refused = await machine.Agent.EnvironmentAsync(machine.Id, before, new AgentEnvironmentRequest(AgentEnvironment.LinuxBoot));

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        await AssertNothingKeptAsync(machine.Id);
    }

    [Fact]
    public async Task RefusesTheEnvironmentTheMachineIsInAndTheInstalledWindows()
    {
        (DeployingMachine machine, _) = await SignedInAsync();
        using DeployingMachine owned = machine;

        HttpResponseMessage same = await AskAsync(machine, AgentEnvironment.WindowsPE);
        HttpResponseMessage installed = await AskAsync(machine, AgentEnvironment.Windows);

        Assert.Equal(HttpStatusCode.Conflict, same.StatusCode);
        Assert.Equal("This machine already started Windows PE.", await TestDatabase.TitleAsync(same));
        Assert.Equal(HttpStatusCode.Conflict, installed.StatusCode);
        Assert.Equal(
            "A machine restarts into Windows PE or the Linux boot environment, not from Windows PE into Windows.",
            await TestDatabase.TitleAsync(installed));
        await AssertNothingKeptAsync(machine.Id);
    }

    [Fact]
    public async Task RefusesAnAgentThatIsTooOldToTakeItsTokenAlong()
    {
        (DeployingMachine machine, _) = await SignedInAsync(sequenceVersion: 3);
        using DeployingMachine owned = machine;

        HttpResponseMessage refused = await AskAsync(machine, AgentEnvironment.LinuxBoot);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(
            "This machine's agent is too old to change the boot environment. Start the machine from the network again, so it gets the server's agent.",
            await TestDatabase.TitleAsync(refused));
        await AssertNothingKeptAsync(machine.Id);
    }

    [Fact]
    public async Task RefusesAnEnvironmentTheServerCannotStart()
    {
        (DeployingMachine machine, _) = await SignedInAsync();
        using DeployingMachine owned = machine;
        application.SetLinuxBoot(false);

        try
        {
            HttpResponseMessage refused = await AskAsync(machine, AgentEnvironment.LinuxBoot);

            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
            Assert.Equal(BootEnvironments.Missing(AgentEnvironment.LinuxBoot), await TestDatabase.TitleAsync(refused));
            await AssertNothingKeptAsync(machine.Id);
        }
        finally
        {
            application.SetLinuxBoot(true);
        }
    }

    [Fact]
    public async Task RefusesASequenceThatIsGoneOrStartsElsewhere()
    {
        (DeployingMachine machine, _) = await SignedInAsync();
        using DeployingMachine owned = machine;
        SequenceView windows = await WindowsAsync();

        HttpResponseMessage gone = await AskAsync(machine, AgentEnvironment.LinuxBoot, Guid.NewGuid());
        HttpResponseMessage elsewhere = await AskAsync(machine, AgentEnvironment.LinuxBoot, windows.Id);

        Assert.Equal(HttpStatusCode.Conflict, gone.StatusCode);
        Assert.Equal("The sequence no longer exists. Choose another sequence.", await TestDatabase.TitleAsync(gone));
        Assert.Equal(HttpStatusCode.Conflict, elsewhere.StatusCode);
        Assert.Equal(
            $"{windows.Name} does not run in the Linux boot environment. It was probably changed after the list was shown. Choose it again.",
            await TestDatabase.TitleAsync(elsewhere));
        await AssertNothingKeptAsync(machine.Id);
    }
}
