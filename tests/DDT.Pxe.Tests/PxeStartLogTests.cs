// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Collections.Frozen;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace DDT.Pxe.Tests;

// The lines an administrator reads to learn what a machine that starts from the network is named.
public sealed class PxeStartLogTests : IDisposable
{
    private const string Offered = "Information 526: Linux boot environment: Tftp linux/shimx64.efi from ";

    private readonly string _boot = Directory.CreateTempSubdirectory("ddt-pxe-start-log-").FullName;
    private readonly FakeLinuxBootSite _site = new();
    private readonly RecordedLog _log = new();

    public PxeStartLogTests() =>
        _site.Current = new LinuxBootBundle(_boot, FrozenSet<string>.Empty, new Uri("https://ddt-server.example.test:8443"), "root");

    public void Dispose() => Directory.Delete(_boot, recursive: true);

    private static int FreePort()
    {
        using Socket socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));

        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }

    private PxeOptions Options(bool tftp, string preferred = "Auto") => new()
    {
        Interfaces = Loopback.InterfaceName,
        BootDirectory = _boot,
        EnableProxyDhcp = false,
        EnableTftp = tftp,
        DefaultBootEnvironment = preferred,
    };

    private async Task<string[]> StartAsync(PxeOptions options, bool catalogue = true)
    {
        PxeListenerBinding binding = new(IPAddress.Loopback, FreePort(), FreePort(), FreePort(), configured => new NetworkInterfaceMap(configured, [Loopback.Interface]));
        PxeHostSource source = new(
            () => new PxeDesiredSetup(1, options, null, false),
            () => new CancellationChangeToken(CancellationToken.None),
            _ => Task.CompletedTask)
        {
            Linux = catalogue ? _site : null,
        };
        using PxeHost host = new(source, TimeProvider.System, _log, binding);

        await host.StartAsync(TestContext.Current.CancellationToken);
        await host.StopAsync(TestContext.Current.CancellationToken);

        return [.. _log.Messages.Where(message => message.Contains("Linux boot environment", StringComparison.Ordinal))];
    }

    [Theory]
    [InlineData("Auto", "Windows PE once a boot image is built, and it until then")]
    [InlineData("WindowsPE", "Windows PE")]
    [InlineData("Linux", "it")]
    public async Task SaysWhereTheLinuxBootEnvironmentIsServedFromAndWhatMachinesWithoutARunStart(string preferred, string expected)
    {
        string[] lines = await StartAsync(Options(tftp: true, preferred));

        Assert.Equal([$"{Offered}{_boot}. Machines without a run start {expected}"], lines);
    }

    [Fact]
    public async Task SaysThatAServerWithoutAUsableLinuxBootEnvironmentOffersNone()
    {
        _site.Current = null;

        string[] lines = await StartAsync(Options(tftp: true));

        Assert.Equal(["Information 527: Linux boot environment: not offered, since this server has none that can be started"], lines);
    }

    // Shim is loaded over TFTP, whatever the boot port serves.
    [Fact]
    public async Task SaysThatWithoutTftpTheLinuxBootEnvironmentIsNotOffered()
    {
        string[] lines = await StartAsync(Options(tftp: false));

        Assert.Equal(["Information 527: Linux boot environment: not offered, since TFTP is off"], lines);
    }

    [Fact]
    public async Task SaysNothingOfItInAHostThatWasGivenNoCatalogue()
    {
        Assert.Empty(await StartAsync(Options(tftp: true), catalogue: false));
    }
}
