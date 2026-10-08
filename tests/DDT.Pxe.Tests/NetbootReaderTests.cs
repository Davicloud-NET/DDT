// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace DDT.Pxe.Tests;

// The loops that receive datagrams never look at the disk. What they answer from is looked at here, in the background.
public sealed class NetbootReaderTests : IDisposable
{
    private const string Failed = "Error 530: Looking at the boot files failed.";
    private const string Again = "Information 531: Looking at the boot files works again";

    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(30);

    private readonly string _boot = Directory.CreateTempSubdirectory("ddt-netboot-reader-").FullName;
    private readonly FakeLinuxBootSite _site = new();
    private readonly RecordedLog _log = new();
    private readonly TestClock _clock = new(DateTimeOffset.FromUnixTimeSeconds(1_791_460_800));

    public void Dispose() => Directory.Delete(_boot, recursive: true);

    private NetbootReader Reader() => new(_site, null, _clock, _log.CreateLogger("netboot"));

    private static async Task UntilAsync(Func<bool> condition)
    {
        using CancellationTokenSource timeout = new(s_timeout);

        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private static int FreePort()
    {
        using Socket socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));

        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }

    [Fact]
    public async Task LooksAgainEveryFiveSecondsUntilItIsStopped()
    {
        Assert.Equal(TimeSpan.FromSeconds(5), NetbootReader.Interval);
        await using NetbootReader reader = Reader();

        reader.Start();
        Assert.Equal(0, _site.Refreshed);

        _clock.Advance(TimeSpan.FromSeconds(4));
        Assert.Equal(0, _site.Refreshed);

        _clock.Advance(TimeSpan.FromSeconds(1));
        await UntilAsync(() => _site.Refreshed == 1 && _clock.Waiting == 1);

        _clock.Advance(TimeSpan.FromSeconds(5));
        await UntilAsync(() => _site.Refreshed == 2 && _clock.Waiting == 1);

        await reader.StopAsync();
        _clock.Advance(TimeSpan.FromMinutes(1));

        Assert.Equal(2, _site.Refreshed);
        Assert.Equal(0, _clock.Waiting);
    }

    // Whatever a look throws, netboot goes on with what the look before found. The log says it once per streak.
    [Fact]
    public async Task ALookThatFailsIsSaidOnceUntilOneWorksAgainAndTheNextIsStillTaken()
    {
        await using NetbootReader reader = Reader();
        _site.Failure = new InvalidOperationException("The disk is gone.");

        reader.Read();
        reader.Read();
        reader.Read();

        Assert.Equal(3, _site.Refreshed);
        Assert.Single(_log.Messages, message => message.StartsWith(Failed, StringComparison.Ordinal));
        Assert.DoesNotContain(Again, _log.Messages);

        _site.Failure = null;
        reader.Read();
        reader.Read();

        Assert.Single(_log.Messages, message => message == Again);

        _site.Failure = new IOException("again");
        reader.Read();

        Assert.Equal(2, _log.Messages.Count(message => message.StartsWith(Failed, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task ALoopWhoseLookFailsGoesOnLooking()
    {
        await using NetbootReader reader = Reader();
        _site.Failure = new InvalidOperationException("The disk is gone.");
        reader.Start();

        _clock.Advance(NetbootReader.Interval);
        await UntilAsync(() => _site.Refreshed == 1 && _clock.Waiting == 1);
        _clock.Advance(NetbootReader.Interval);
        await UntilAsync(() => _site.Refreshed == 2 && _clock.Waiting == 1);

        Assert.Single(_log.Messages, message => message.StartsWith(Failed, StringComparison.Ordinal));
    }

    // The host takes the first look itself before a listener starts, so the first datagram already finds an answer.
    [Fact]
    public async Task TheHostLooksBeforeItsListenersStartThenEveryFiveSecondsAndNoLongerOnceStopped()
    {
        _site.Current = TestBundle.Of(_boot);
        PxeListenerBinding binding = new(IPAddress.Loopback, FreePort(), FreePort(), FreePort(), configured => new NetworkInterfaceMap(configured, [Loopback.Interface]));
        PxeOptions options = new() { Interfaces = Loopback.InterfaceName, BootDirectory = _boot, EnableProxyDhcp = false, EnableTftp = false };
        PxeHostSource source = new(
            () => new PxeDesiredSetup(1, options, null, false),
            () => new CancellationChangeToken(CancellationToken.None),
            _ => Task.CompletedTask)
        {
            Linux = _site,
        };
        using PxeHost host = new(source, _clock, _log, binding);

        await host.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, _site.Refreshed);
        Assert.True(host.Files!.LinuxAvailable);

        _clock.Advance(NetbootReader.Interval);
        await UntilAsync(() => _site.Refreshed == 2 && _clock.Waiting == 1);

        await host.StopAsync(TestContext.Current.CancellationToken);
        _clock.Advance(TimeSpan.FromMinutes(1));

        Assert.Equal(2, _site.Refreshed);
    }

    // A first look that fails must not keep the listeners from starting: Windows PE still has to be served.
    [Fact]
    public async Task AHostWhoseFirstLookFailsStillStarts()
    {
        _site.Failure = new InvalidOperationException("The disk is gone.");
        PxeListenerBinding binding = new(IPAddress.Loopback, FreePort(), FreePort(), FreePort(), configured => new NetworkInterfaceMap(configured, [Loopback.Interface]));
        PxeOptions options = new() { Interfaces = Loopback.InterfaceName, BootDirectory = _boot, EnableProxyDhcp = false, EnableTftp = false };
        List<PxeApplyResult> results = [];
        PxeHostSource source = new(
            () => new PxeDesiredSetup(1, options, null, false),
            () => new CancellationChangeToken(CancellationToken.None),
            result =>
            {
                results.Add(result);

                return Task.CompletedTask;
            })
        {
            Linux = _site,
        };
        using PxeHost host = new(source, _clock, _log, binding);

        await host.StartAsync(TestContext.Current.CancellationToken);

        Assert.True(Assert.Single(results).Succeeded);
        Assert.NotNull(host.Files);
        Assert.False(host.Files.LinuxAvailable);
        Assert.Contains(_log.Messages, message => message.StartsWith(Failed, StringComparison.Ordinal));

        await host.StopAsync(TestContext.Current.CancellationToken);
    }
}
