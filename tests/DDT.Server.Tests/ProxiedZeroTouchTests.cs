using DDT.Contracts.Machines;
using Xunit;

namespace DDT.Server.Tests;

// Behind a listed proxy, zero touch judges the address the proxy reports, never the proxy's own.
public sealed class ProxiedZeroTouchTests(ProxiedZeroTouchApplication application, ProxyInZeroTouchNetworkApplication overlapping)
    : IClassFixture<ProxiedZeroTouchApplication>, IClassFixture<ProxyInZeroTouchNetworkApplication>
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
        Assert.Equal(deployment, (await machine.NextAsync()).Deployment?.Id);
    }

    [Fact]
    public async Task ANetbootThroughAProxyFromElsewhereWaitsForASignIn()
    {
        using DeployingMachine machine = await DeployingMachine.RegisterAsync(application);
        await ZeroTouchTests.AssignWhileAwayAsync(application, machine);
        using AgentClient elsewhere = ProxiedApplication.Agent(application, "203.0.113.20");

        Assert.Equal(MachineState.Pending, (await machine.RegisterAgainAsync(elsewhere)).State);
        Assert.Equal("203.0.113.20", (await application.MachineAsync(machine.Id)).LastSeenAddress);
        Assert.Null((await machine.NextAsync()).Deployment);
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
        Assert.Equal(deployment, (await machine.NextAsync()).Deployment?.Id);
    }

    // A request the proxy forwards without X-Forwarded-For, from an nginx location that dropped it for example, stays at
    // the proxy's own address. A reported address in the same network afterwards shows that only the proxy's did not count.
    [Theory]
    [InlineData(ProxiedApplication.Proxy)]
    [InlineData("::ffff:" + ProxiedApplication.Proxy)]
    [InlineData(ProxiedApplication.Ipv6Proxy)]
    [InlineData("198.51.100.7")]
    public async Task ANetbootThatStillComesFromAListedProxyWaitsForASignIn(string proxy)
    {
        using DeployingMachine machine = await DeployingMachine.RegisterAsync(overlapping);
        Guid deployment = await ZeroTouchTests.AssignWhileAwayAsync(overlapping, machine);
        using AgentClient unreported = new(overlapping.CreateDefaultClient(), proxy);

        Assert.Equal(MachineState.Pending, (await machine.RegisterAgainAsync(unreported)).State);
        Assert.Equal(proxy, (await overlapping.MachineAsync(machine.Id)).LastSeenAddress);
        Assert.Null((await machine.NextAsync()).Deployment);

        using AgentClient reported = ProxiedApplication.Agent(overlapping, "192.0.2.50", proxy);

        Assert.Equal(MachineState.Approved, (await machine.RegisterAgainAsync(reported)).State);
        Assert.Equal(deployment, (await machine.NextAsync()).Deployment?.Id);
    }
}
