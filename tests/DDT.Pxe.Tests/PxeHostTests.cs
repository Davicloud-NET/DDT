// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Sockets;
using DDT.Contracts.Messages;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace DDT.Pxe.Tests;

// The listeners follow the settings. A new version restarts them with the new setup. A setup whose sockets don't bind
// is replaced by the one before. Loopback ports stand in for 67, 4011 and 69.
public sealed class PxeHostTests : IDisposable
{
    private readonly string _boot = Directory.CreateTempSubdirectory("ddt-pxe-host-").FullName;
    private readonly PxeListenerBinding _binding;
    private readonly List<PxeApplyResult> _results = [];
    private int _scans;
    private PxeDesiredSetup _desired;
    private CancellationTokenSource _changed = new();

    public PxeHostTests()
    {
        _binding = new PxeListenerBinding(IPAddress.Loopback, FreePort(), FreePort(), FreePort(), configured =>
        {
            Interlocked.Increment(ref _scans);

            return new NetworkInterfaceMap(configured, [Loopback.Interface]);
        });
        _desired = Desired(1, tftp: true, dhcp: false);
    }

    [Fact]
    public async Task ANewVersionRestartsTheListenersWithIt()
    {
        using PxeHost host = Host();
        await host.StartAsync(TestContext.Current.CancellationToken);

        Assert.True(Assert.Single(_results).Succeeded);
        Assert.True(IsBound(_binding.TftpPort));
        Assert.False(IsBound(_binding.DhcpPort));

        await ChangeAsync(Desired(2, tftp: false, dhcp: true), results: 2);

        Assert.True(_results[^1].Succeeded);
        Assert.Equal(2, _results[^1].Version);
        Assert.False(IsBound(_binding.TftpPort));
        Assert.True(IsBound(_binding.DhcpPort));
        Assert.True(IsBound(_binding.BootServerPort));
        Assert.Same(host.Applied!.Interfaces, _results[^1].Interfaces);

        await host.StopAsync(TestContext.Current.CancellationToken);

        Assert.False(IsBound(_binding.DhcpPort));
        Assert.Null(host.Applied);
    }

    // Interfaces are found again at every apply. So a rescan, which only raises the version, picks up a changed
    // address.
    [Fact]
    public async Task EveryApplyFindsTheInterfacesAgain()
    {
        using PxeHost host = Host();
        await host.StartAsync(TestContext.Current.CancellationToken);

        await ChangeAsync(Desired(2, tftp: true, dhcp: false), results: 2);

        Assert.Equal(2, _scans);
    }

    // The previous setup comes back, and the failure goes to the source instead of stopping the host.
    [Fact]
    public async Task ASetupThatDoesNotBindGivesWayToThePreviousOne()
    {
        using PxeHost host = Host();
        await host.StartAsync(TestContext.Current.CancellationToken);
        PxeSetup running = host.Applied!;
        using Socket taken = Hold(_binding.DhcpPort);

        await ChangeAsync(Desired(2, tftp: true, dhcp: true), results: 2);

        PxeApplyResult failed = _results[^1];
        Assert.False(failed.Succeeded);
        Assert.Equal(2, failed.Version);
        Assert.Contains($"could not bind UDP {_binding.DhcpPort} for ProxyDHCP", failed.Message, StringComparison.Ordinal);
        Assert.Equal(ServerMessages.SettingsApplyPxeBindFailed.Code, failed.Text?.Code);
        Assert.Equal("proxyDhcp", failed.Text?.Args["protocol"]);
        Assert.Same(running, host.Applied);
        Assert.True(IsBound(_binding.TftpPort));

        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    // When nothing ran before, the listeners stay stopped. The host only stops when configuration named what to serve.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AtStartupABindFailureStopsTheHostOnlyWhenConfigurationDecided(bool fromConfiguration)
    {
        using Socket taken = Hold(_binding.TftpPort);
        _desired = Desired(1, tftp: true, dhcp: false) with { StopHostOnFailure = fromConfiguration };
        using PxeHost host = Host();

        if (fromConfiguration)
        {
            InvalidOperationException refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync(TestContext.Current.CancellationToken));
            Assert.Contains($"could not bind UDP {_binding.TftpPort} for TFTP", refusal.Message, StringComparison.Ordinal);
        }
        else
        {
            await host.StartAsync(TestContext.Current.CancellationToken);
            Assert.False(Assert.Single(_results).Succeeded);
        }

        Assert.Null(host.Applied);
    }

    // A section with problems serves nothing, and says why.
    [Fact]
    public async Task NoOptionsServeNothing()
    {
        using PxeHost host = Host();
        await host.StartAsync(TestContext.Current.CancellationToken);

        ServerMessage refusal = ServerMessages.SettingsApplyPxeClosed.With("problems", ServerMessages.SettingsAtLeastOne.With());
        await ChangeAsync(new PxeDesiredSetup(2, null, refusal, false), results: 2);

        Assert.Null(host.Applied);
        Assert.False(IsBound(_binding.TftpPort));
        Assert.Equal("The pxe settings have problems, so nothing is served until they are fixed: Must be at least 1.", _results[^1].Message);
        Assert.Same(refusal, _results[^1].Text);
        Assert.False(_results[^1].Succeeded);
    }

    public void Dispose()
    {
        _changed.Dispose();
        Directory.Delete(_boot, recursive: true);
    }

    private PxeHost Host() => new(
        new PxeHostSource(
            () => _desired,
            () => new CancellationChangeToken(_changed.Token),
            result =>
            {
                lock (_results)
                {
                    _results.Add(result);
                }

                return Task.CompletedTask;
            }),
        TimeProvider.System,
        NullLoggerFactory.Instance,
        _binding);

    private async Task ChangeAsync(PxeDesiredSetup desired, int results)
    {
        _desired = desired;
        CancellationTokenSource changed = _changed;
        _changed = new CancellationTokenSource();
        await changed.CancelAsync();

        for (int attempt = 0; attempt < 200 && Count() < results; attempt++)
        {
            await Task.Delay(25, TestContext.Current.CancellationToken);
        }

        Assert.Equal(results, Count());
        changed.Dispose();
    }

    private int Count()
    {
        lock (_results)
        {
            return _results.Count;
        }
    }

    private PxeDesiredSetup Desired(long version, bool tftp, bool dhcp) =>
        new(version, new PxeOptions { Interfaces = Loopback.InterfaceName, BootDirectory = _boot, EnableTftp = tftp, EnableProxyDhcp = dhcp }, null, false);

    private static int FreePort()
    {
        using Socket socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));

        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }

    private static Socket Hold(int port)
    {
        Socket socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, port));

        return socket;
    }

    private static bool IsBound(int port)
    {
        try
        {
            using Socket probe = Hold(port);

            return false;
        }
        catch (SocketException)
        {
            return true;
        }
    }
}
