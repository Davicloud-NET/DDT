// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace DDT.Pxe.Tests;

// The lines an administrator reads to learn what a machine that starts from the network is named.
public sealed class PxeStartLogTests : IDisposable
{
    private const string Offered = "Information 526: Linux boot environment: Tftp linux/shimx64.efi from ";
    private const string BootManager = "x64/bootmgfw.efi";

    private readonly string _boot = Directory.CreateTempSubdirectory("ddt-pxe-start-log-").FullName;
    private readonly FakeLinuxBootSite _site = new();
    private readonly RecordedLog _log = new();

    public PxeStartLogTests() =>
        _site.Current = TestBundle.Of(_boot);

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

    // The boot target a new server has, which a boot image build brings the file of.
    private PxeOptions WithBootTarget(string preferred, string? server = null)
    {
        PxeOptions options = Options(tftp: true, preferred);
        options.BootTargets["X64Uefi"] = new BootTargetOptions { Method = "Tftp", BootFile = BootManager, ServerAddress = server };

        return options;
    }

    private void ABootImageIsBuilt()
    {
        Directory.CreateDirectory(Path.Combine(_boot, "x64"));
        File.WriteAllText(Path.Combine(_boot, BootManager), "boot manager");
    }

    private async Task<string[]> StartAsync(PxeOptions options, bool catalogue = true, string about = "Linux boot environment:")
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

        return [.. _log.Messages.Where(message => message.Contains(about, StringComparison.Ordinal))];
    }

    // What is said of the boot manager's file, when it is said at all.
    private Task<string[]> StartForBootManagerAsync(PxeOptions options) => StartAsync(options, about: $" names {BootManager}");

    [Theory]
    [InlineData("Auto", "it until a boot image is built")]
    [InlineData("WindowsPE", "Windows PE")]
    [InlineData("Linux", "it")]
    public async Task SaysWhereTheLinuxBootEnvironmentIsServedFromAndWhatMachinesWithoutARunStart(string preferred, string expected)
    {
        string[] lines = await StartAsync(WithBootTarget(preferred));

        Assert.Equal([$"{Offered}{_boot}. Machines without a run start {expected}"], lines);
    }

    [Fact]
    public async Task SaysThatAutoStartsWindowsPEOnceABootImageIsThere()
    {
        ABootImageIsBuilt();

        string[] lines = await StartAsync(WithBootTarget("Auto"));

        Assert.Equal([$"{Offered}{_boot}. Machines without a run start Windows PE, since a boot image is there"], lines);
    }

    // DDT cannot look into another TFTP server, and names Windows PE to it as before.
    [Fact]
    public async Task SaysThatAutoStartsWindowsPEWhereItsBootTargetNamesAnotherServer()
    {
        string[] lines = await StartAsync(WithBootTarget("Auto", server: "192.0.2.7"));

        Assert.Equal([$"{Offered}{_boot}. Machines without a run start Windows PE, since its boot target names another server"], lines);
    }

    [Theory]
    [InlineData("Auto")]
    [InlineData("Linux")]
    public async Task OnlyInformsOfAMissingBootManagerWhileMachinesStartTheLinuxBootEnvironment(string preferred)
    {
        string[] lines = await StartForBootManagerAsync(WithBootTarget(preferred));

        Assert.Equal(
            [$"Information 532: Boot target X64Uefi names {BootManager}, which comes with the boot image. Until one is built, machines start the Linux boot environment"],
            lines);
    }

    [Fact]
    public async Task WarnsOfAMissingBootManagerThatMachinesWithoutARunAreNamed()
    {
        string[] lines = await StartForBootManagerAsync(WithBootTarget("WindowsPE"));

        Assert.Equal([$"Warning 504: Boot target X64Uefi names {BootManager}, which is not in {_boot}"], lines);
    }

    [Fact]
    public async Task WarnsOfAMissingBootManagerWhereNoLinuxBootEnvironmentIsOffered()
    {
        _site.Current = null;

        string[] lines = await StartForBootManagerAsync(WithBootTarget("Auto"));

        Assert.Equal([$"Warning 504: Boot target X64Uefi names {BootManager}, which is not in {_boot}"], lines);
    }

    [Fact]
    public async Task SaysNothingOfABootManagerThatIsThere()
    {
        ABootImageIsBuilt();

        Assert.Empty(await StartForBootManagerAsync(WithBootTarget("Auto")));
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
