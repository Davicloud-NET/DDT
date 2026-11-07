// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Security.Cryptography;
using DDT.Contracts.Agents;
using DDT.Contracts.Images;
using DDT.Contracts.Sequences;
using DDT.Pxe;
using DDT.Server.Authentication;
using DDT.Server.Configuration;
using DDT.Server.Deployments;
using DDT.Server.Images;
using DDT.Server.LinuxBoot;
using DDT.Server.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DDT.Server.Tests;

// What netboot learns from the database about each machine: its run's boot environment, the restart it was asked for,
// and whether its firmware would refuse the shim. The shim here is a real one, signed under Microsoft's 2011 CA.
public sealed class BootChoiceRefresherTests : IClassFixture<BothEnvironmentsApplication>
{
    private readonly BothEnvironmentsApplication _application;
    private readonly BootChoices _choices;
    private readonly BootChoiceRefresher _refresher;

    public BootChoiceRefresherTests(BothEnvironmentsApplication application)
    {
        _application = application;
        _choices = application.Services.GetRequiredService<BootChoices>();
        _refresher = ActivatorUtilities.CreateInstance<BootChoiceRefresher>(application.Services);
        TestLinuxBoot.Write(application.LinuxBootFolder, TestPe.Fixture("shimx64.efi.dualsigned"));
    }

    private async Task<BootWish> WishAsync(DeployingMachine machine)
    {
        await _refresher.RefreshAsync(TestContext.Current.CancellationToken);

        return _choices.For(new NetbootMachine(machine.Registration.PrimaryMac, ReadOnlyMemory<byte>.Empty));
    }

    private async Task<SequenceView> LinuxAsync()
    {
        Image image = await _application.SeedRawImageAsync(RandomNumberGenerator.GetBytes(4096), name: $"noble {Guid.NewGuid():N}");

        return await (await _application.AdministratorAsync()).CreatedSequenceAsync(SequenceRequests.Linux(image.Id));
    }

    private async Task<SequenceView> WindowsAsync()
    {
        Image image = await _application.SeedImageAsync(RandomNumberGenerator.GetBytes(4096));

        return await (await _application.AdministratorAsync()).CreatedSequenceAsync(SequenceRequests.Minimal(image.Id));
    }

    private async Task<DeployingMachine> ApprovedAsync(Func<AgentRegistration, AgentRegistration> change)
    {
        DeployingMachine machine = await DeployingMachine.RegisterAsync(_application, change);
        (await (await _application.AdministratorAsync()).PostAsync($"/api/machines/{machine.Id}/approve")).EnsureSuccessStatusCode();
        await machine.NextAsync();

        return machine;
    }

    private static AgentRegistration WindowsPE(AgentRegistration registration) => registration with { Disks = [DeployingMachine.Disk(0)] };

    [Fact]
    public async Task AMachineWithoutARunOrARestartHasNoWish()
    {
        using DeployingMachine machine = await ApprovedAsync(WindowsPE);

        Assert.Equal(BootWish.None, await WishAsync(machine));
    }

    // The machine sits in Windows PE and is assigned a Linux sequence: its next netboot must name the shim.
    [Fact]
    public async Task AnAssignedRunNamesTheBootEnvironmentItsSequenceStartsIn()
    {
        using DeployingMachine inWindowsPE = await ApprovedAsync(WindowsPE);
        using DeployingMachine inLinux = await ApprovedAsync(DeployingMachine.LinuxBoot());
        SignedInClient administrator = await _application.AdministratorAsync();

        await administrator.AssignedAsync(inWindowsPE.Id, (await LinuxAsync()).Id, "LINUX-01");
        await administrator.AssignedAsync(inLinux.Id, (await WindowsAsync()).Id, "WIN-01");

        Assert.Equal(AgentEnvironment.LinuxBoot, (await WishAsync(inWindowsPE)).Wanted);
        Assert.Equal(AgentEnvironment.WindowsPE, (await WishAsync(inLinux)).Wanted);
    }

    // A run assigned before the server kept the boot environment says it through its sequence.
    [Fact]
    public async Task ARunFromBeforeTheColumnStillNamesItsBootEnvironment()
    {
        using DeployingMachine machine = await ApprovedAsync(WindowsPE);
        await (await _application.AdministratorAsync()).AssignedAsync(machine.Id, (await LinuxAsync()).Id, "LINUX-02");
        await _application.QueryAsync(database => database.Deployments
            .Where(run => run.MachineId == machine.Id)
            .ExecuteUpdateAsync(run => run.SetProperty(row => row.BootEnvironment, (AgentEnvironment?)null), TestContext.Current.CancellationToken));

        Assert.Equal(AgentEnvironment.LinuxBoot, (await WishAsync(machine)).Wanted);
    }

    [Fact]
    public async Task ARestartTheMachineWasAskedForCountsInsideItsWindowOnly()
    {
        string operatorName = await _application.CreateUserAsync(DdtRoleNames.Operator);
        using DeployingMachine machine = await DeployingMachine.SignedInAsync(_application, operatorName, [DeployingMachine.Disk(0)]);
        HttpResponseMessage asked = await machine.Agent.EnvironmentAsync(machine.Id, machine.Token, new AgentEnvironmentRequest(AgentEnvironment.LinuxBoot, null));
        Assert.Equal(HttpStatusCode.NoContent, asked.StatusCode);

        Assert.Equal(AgentEnvironment.LinuxBoot, (await WishAsync(machine)).Wanted);

        _application.Clock.Advance(DeploymentLimits.EnvironmentSwitch + TimeSpan.FromSeconds(1));

        // The row still says where to, since the machine never came back. The time alone ends it.
        Assert.Equal(AgentEnvironment.LinuxBoot, (await _application.MachineAsync(machine.Id)).NextEnvironment);
        Assert.Null((await WishAsync(machine)).Wanted);
    }

    // Rows: Secure Boot as the machine reported it, the CAs its firmware trusts, and whether the shim is refused.
    [Theory]
    [InlineData(true, UefiCa.Microsoft2023, true)]
    [InlineData(true, UefiCa.None, true)]
    [InlineData(true, UefiCa.Microsoft2011, false)]
    [InlineData(true, UefiCa.Microsoft2011 | UefiCa.Microsoft2023, false)]
    [InlineData(true, null, false)]
    [InlineData(false, UefiCa.None, false)]
    [InlineData(null, UefiCa.None, false)]
    public async Task AMachineWhoseFirmwareTrustsNoneOfTheShimsAuthoritiesWithSecureBootOnIsRefusedLinux(bool? secureBoot, UefiCa? trusted, bool refused)
    {
        Assert.Equal(UefiCa.Microsoft2011, _application.Services.GetRequiredService<LinuxBootEnvironment>().Current.ShimSignedUnder);
        using DeployingMachine machine = await ApprovedAsync(DeployingMachine.LinuxBoot(secureBoot, trusted));

        Assert.Equal(new BootWish(null, refused), await WishAsync(machine));
    }

    // Until someone approves it, a row is only what a registration from anywhere on the network claimed.
    [Fact]
    public async Task AMachineNobodyEverApprovedIsNotRefusedLinuxWhateverItsRegistrationSays()
    {
        using DeployingMachine machine = await DeployingMachine.RegisterAsync(_application, DeployingMachine.LinuxBoot(true, UefiCa.None));

        Assert.Equal(BootWish.None, await WishAsync(machine));

        (await (await _application.AdministratorAsync()).PostAsync($"/api/machines/{machine.Id}/approve")).EnsureSuccessStatusCode();

        Assert.Equal(new BootWish(null, true), await WishAsync(machine));
    }

    [Fact]
    public async Task AMachineIsFoundByItsUuidWhenAnotherAdapterAsks()
    {
        using DeployingMachine machine = await ApprovedAsync(WindowsPE);
        await (await _application.AdministratorAsync()).AssignedAsync(machine.Id, (await LinuxAsync()).Id, "LINUX-03");
        await _refresher.RefreshAsync(TestContext.Current.CancellationToken);
        Guid uuid = Guid.Parse(machine.Registration.SmbiosUuid);

        Assert.Equal(AgentEnvironment.LinuxBoot, _choices.For(new NetbootMachine("AABBCCDDEEFF", uuid.ToByteArray())).Wanted);
        Assert.Equal(AgentEnvironment.LinuxBoot, _choices.For(new NetbootMachine("AABBCCDDEEFF", uuid.ToByteArray(bigEndian: true))).Wanted);
    }

    [Fact]
    public async Task AMachineThatWasRemovedIsForgotten()
    {
        using DeployingMachine machine = await ApprovedAsync(WindowsPE);
        SignedInClient administrator = await _application.AdministratorAsync();
        await administrator.AssignedAsync(machine.Id, (await LinuxAsync()).Id, "LINUX-04");
        Assert.Equal(AgentEnvironment.LinuxBoot, (await WishAsync(machine)).Wanted);

        await _application.QueryAsync(async database =>
        {
            await database.Deployments.Where(run => run.MachineId == machine.Id).ExecuteDeleteAsync(TestContext.Current.CancellationToken);

            return await database.Machines.Where(row => row.Id == machine.Id).ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        });

        Assert.Equal(BootWish.None, await WishAsync(machine));
    }

    // The packet path never asks the database. When the database fails, netboot goes on with what it knew.
    [Fact]
    public async Task ARefreshThatFailsLeavesTheListAsItWas()
    {
        using DeployingMachine machine = await ApprovedAsync(WindowsPE);
        await (await _application.AdministratorAsync()).AssignedAsync(machine.Id, (await LinuxAsync()).Id, "LINUX-05");
        Assert.Equal(AgentEnvironment.LinuxBoot, (await WishAsync(machine)).Wanted);
        BootChoiceRefresher broken = Refresher(new FailingScopes(), NullLogger<BootChoiceRefresher>.Instance, "web,pxe");

        await Assert.ThrowsAsync<InvalidOperationException>(() => broken.RefreshAsync(TestContext.Current.CancellationToken));

        Assert.Equal(AgentEnvironment.LinuxBoot, _choices.For(new NetbootMachine(machine.Registration.PrimaryMac, ReadOnlyMemory<byte>.Empty)).Wanted);
    }

    [Fact]
    public async Task TheServiceSurvivesADatabaseThatIsDownAndSaysSoOnce()
    {
        using RecordingLoggerProvider log = new();
        using ILoggerFactory loggers = LoggerFactory.Create(logging => logging.AddProvider(log));
        BootChoices choices = new(TimeProvider.System);
        FailingScopes scopes = new();
        using BootChoiceRefresher service = new(
            choices,
            _application.Services.GetRequiredService<LinuxBootEnvironment>(),
            scopes,
            Options.Create(new DdtOptions { Roles = "web,pxe" }),
            TimeProvider.System,
            loggers.CreateLogger<BootChoiceRefresher>());

        await service.StartAsync(TestContext.Current.CancellationToken);
        await log.WaitForAsync("Could not read which boot environment each machine starts", TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        // A change makes it read again at once, which fails again and is not said again.
        choices.Changed();
        await SettingsPxeTests.Eventually(() => Task.FromResult(scopes.Asked >= 2));
        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Single(log.Entries, entry => entry.EventId.Id == 986);
        Assert.Equal(BootWish.None, choices.For(new NetbootMachine("525400DD7001", ReadOnlyMemory<byte>.Empty)));
    }

    // A host without the pxe role has nobody to ask the list, so it never reads the database for it.
    [Fact]
    public async Task AHostWithoutThePxeRoleReadsNothing()
    {
        using BootChoiceRefresher service = Refresher(new FailingScopes(), NullLogger<BootChoiceRefresher>.Instance, "web");

        await service.StartAsync(TestContext.Current.CancellationToken);
        await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.True(service.ExecuteTask.IsCompletedSuccessfully);
    }

    [Fact]
    public void TheListenersAreGivenTheListAndTheLinuxBootEnvironment()
    {
        PxeHostSource source = PxeSettingsSource.Create(_application.Services);

        Assert.Same(_choices, source.Choice);
        Assert.Same(_application.Services.GetRequiredService<LinuxBootSite>(), source.Linux);
    }

    private BootChoiceRefresher Refresher(IServiceScopeFactory scopes, ILogger<BootChoiceRefresher> logger, string roles) => new(
        _choices,
        _application.Services.GetRequiredService<LinuxBootEnvironment>(),
        scopes,
        Options.Create(new DdtOptions { Roles = roles }),
        TimeProvider.System,
        logger);

    private sealed class FailingScopes : IServiceScopeFactory
    {
        private int _asked;

        public int Asked => Volatile.Read(ref _asked);

        public IServiceScope CreateScope()
        {
            Interlocked.Increment(ref _asked);

            throw new InvalidOperationException("The database is down.");
        }
    }
}
