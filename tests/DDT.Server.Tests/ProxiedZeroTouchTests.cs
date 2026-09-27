// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Machines;
using Xunit;

namespace DDT.Server.Tests;

// Behind a listed proxy, zero touch judges the address the proxy reports, never the proxy's own.
public sealed class ProxiedZeroTouchTests(ProxiedZeroTouchApplication application)
    : IClassFixture<ProxiedZeroTouchApplication>
{
    [Theory]
    [InlineData(ProxiedApplication.Proxy, "10.200.3.4")]
    [InlineData(ProxiedApplication.Ipv6Proxy, "fd00:200::42")]
    [InlineData("198.51.100.7", "10.200.3.5")]
    public async Task ANetbootThroughAProxyFromAListedNetworkKeepsTheAssignmentAuthorized(string proxy, string reported)
    {
        using DeployingMachine machine = await DeployingMachine.RegisterAsync(application);
        Guid deployment = await ZeroTouchTests.AssignWhileAwayAsync(application, machine);
        using AgentClient lab = ProxiedApplication.Agent(application, reported, proxy);

        Assert.Equal(MachineState.Approved, (await machine.RegisterAgainAsync(lab)).State);
        Assert.Equal(reported, (await application.MachineAsync(machine.Id)).LastSeenAddress);
        Assert.Equal(deployment, (await application.MachineAsync(machine.Id)).ActiveDeploymentId);
    }

    [Fact]
    public async Task ANetbootThroughAProxyFromElsewhereWaitsForASignIn()
    {
        using DeployingMachine machine = await DeployingMachine.RegisterAsync(application);
        await ZeroTouchTests.AssignWhileAwayAsync(application, machine);
        using AgentClient elsewhere = ProxiedApplication.Agent(application, "203.0.113.20");

        Assert.Equal(MachineState.Pending, (await machine.RegisterAgainAsync(elsewhere)).State);
        Assert.Equal("203.0.113.20", (await application.MachineAsync(machine.Id)).LastSeenAddress);
        Assert.Null((await machine.NextAsync()).Run);
    }

    // The same header through the proxy afterwards shows that it was the connection, not the address, that did not count.
    [Fact]
    public async Task AClientThatIsNotAProxyCannotClaimAListedNetwork()
    {
        using DeployingMachine machine = await DeployingMachine.RegisterAsync(application);
        Guid deployment = await ZeroTouchTests.AssignWhileAwayAsync(application, machine);
        using AgentClient spoofing = ProxiedApplication.Agent(application, "10.200.3.6", connection: "203.0.113.21");

        Assert.Equal(MachineState.Pending, (await machine.RegisterAgainAsync(spoofing)).State);
        Assert.Equal("203.0.113.21", (await application.MachineAsync(machine.Id)).LastSeenAddress);

        using AgentClient lab = ProxiedApplication.Agent(application, "10.200.3.6");

        Assert.Equal(MachineState.Approved, (await machine.RegisterAgainAsync(lab)).State);
        Assert.Equal(deployment, (await application.MachineAsync(machine.Id)).ActiveDeploymentId);
    }

    // A request the proxy forwards without X-Forwarded-For, from an nginx location that dropped it for example, stays at
    // the proxy's own address, so a zero touch network that holds a listed proxy is refused: configured, the server does
    // not start.
    [Fact]
    public void AZeroTouchNetworkThatHoldsAListedProxyStopsTheServer()
    {
        using ProxyInZeroTouchNetworkApplication overlapping = new();

        InvalidOperationException refusal = Assert.Throws<InvalidOperationException>(() => overlapping.CreateClient());

        Assert.Contains($"DDT:Machines:ZeroTouchNetworks: The zero touch network 192.0.2.0/24 contains the proxy {ProxiedApplication.Proxy}.", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("DDT:Machines:ZeroTouchNetworks: The zero touch network 198.51.100.0/24 overlaps the proxy network 198.51.100.0/24.", refusal.Message, StringComparison.Ordinal);
    }
}
