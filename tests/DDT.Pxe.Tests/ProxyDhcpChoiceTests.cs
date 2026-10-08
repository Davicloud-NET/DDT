// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Collections.Frozen;
using System.Net;
using DDT.Contracts.Agents;
using DDT.Protocols.Dhcp;
using DDT.Protocols.Pxe;
using Xunit;

namespace DDT.Pxe.Tests;

// Which boot file ProxyDHCP names, from the datagram to the answer: the machine's run or pending restart first, then
// the default of the settings, and only what the server can start.
public sealed class ProxyDhcpChoiceTests : IDisposable
{
    private const int ServedIndex = 25;
    private const string WindowsPE = "x64/bootmgfw.efi";
    private const string Shim = "linux/shimx64.efi";

    private static readonly IPAddress s_served = IPAddress.Parse("172.24.64.1");
    private static readonly IPAddress s_relay = IPAddress.Parse("192.0.2.1");

    private readonly string _root = Directory.CreateTempSubdirectory("ddt-choice-").FullName;
    private readonly FakeLinuxBootSite _site = new();
    private readonly FakeBootChoice _choice = new();
    private readonly TestClock _clock = new(DateTimeOffset.FromUnixTimeSeconds(1_791_460_800));

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void WindowsPEIsBuilt()
    {
        Directory.CreateDirectory(Path.Combine(_root, "boot", "x64"));
        File.WriteAllText(Path.Combine(_root, "boot", "x64", "bootmgfw.efi"), "boot manager");
    }

    private void LinuxIsThere() =>
        _site.Current = new LinuxBootBundle(_root, FrozenSet<string>.Empty, new Uri("https://ddt-server.example.test:8443"), "root");

    private ProxyDhcpHandler Handler(string preferred = "Auto", Action<PxeOptions>? configure = null)
    {
        NetworkInterfaceMap map = new("lab", [new ServedInterface(ServedIndex, "lab", s_served)]);
        PxeOptions options = new()
        {
            BootDirectory = Path.Combine(_root, "boot"),
            DefaultBootEnvironment = preferred,
            AuthorisedRelayAgents = s_relay.ToString(),
            BootTargets =
            {
                ["X64Uefi"] = new BootTargetOptions { Method = "Tftp", BootFile = WindowsPE },
                ["X64UefiHttp"] = new BootTargetOptions { Method = "Http", BootFile = "http://172.24.64.1:8080/boot/x64/bootmgfw.efi" },
                ["X86Bios"] = new BootTargetOptions { Method = "Tftp", BootFile = "bios/pxeboot.n12" },
            },
        };
        configure?.Invoke(options);

        PxeSetup setup = PxeSetup.Create(options, map);
        BootFiles files = new(setup.Files, _site, 8080, new BootConnections(_clock), _clock);

        return new ProxyDhcpHandler(setup.ProxyDhcp, map, null, new NetbootChoice(setup, _choice, files, _clock));
    }

    private static ProxyDhcpOutcome Discover(ProxyDhcpHandler handler, string fixture = "discover-uefi-x64-pxeclient") =>
        handler.Handle(
            Loopback.Fixture("Dhcp", fixture),
            ProxyDhcpListenPort.Dhcp,
            new DatagramArrival(ServedIndex, IPAddress.Broadcast, new IPEndPoint(IPAddress.Any, 68)),
            new byte[1500]);

    private static string? Named(ProxyDhcpOutcome outcome) => outcome.Kind == ProxyDhcpOutcomeKind.Replied ? outcome.Reply!.BootFileName : null;

    // Rows: the wish, the default, whether a boot image is built, whether the bundle is usable, and the file named.
    [Theory]
    [InlineData(AgentEnvironment.LinuxBoot, "Auto", true, true, Shim)]
    [InlineData(AgentEnvironment.LinuxBoot, "WindowsPE", true, true, Shim)]
    [InlineData(AgentEnvironment.LinuxBoot, "WindowsPE", false, true, Shim)]
    [InlineData(AgentEnvironment.LinuxBoot, "Auto", true, false, WindowsPE)]
    [InlineData(AgentEnvironment.WindowsPE, "Linux", true, true, WindowsPE)]
    [InlineData(AgentEnvironment.WindowsPE, "Linux", false, true, Shim)]
    [InlineData(null, "Auto", true, true, WindowsPE)]
    [InlineData(null, "Auto", false, true, Shim)]
    [InlineData(null, "Auto", true, false, WindowsPE)]
    [InlineData(null, "Auto", false, false, WindowsPE)]
    [InlineData(null, "WindowsPE", true, true, WindowsPE)]
    [InlineData(null, "WindowsPE", false, true, WindowsPE)]
    [InlineData(null, "Linux", true, true, Shim)]
    [InlineData(null, "Linux", false, true, Shim)]
    [InlineData(null, "Linux", true, false, WindowsPE)]
    [InlineData(null, "linux", false, true, Shim)]
    public void NamesTheBootFileOfTheEnvironmentTheMachineShouldStart(AgentEnvironment? wanted, string preferred, bool built, bool bundle, string expected)
    {
        if (built)
        {
            WindowsPEIsBuilt();
        }

        if (bundle)
        {
            LinuxIsThere();
        }

        _choice.Wish = new BootWish(wanted, false);

        ProxyDhcpOutcome outcome = Discover(Handler(preferred));

        Assert.Equal(expected, Named(outcome));
        Assert.Equal(DhcpMessageType.Offer, outcome.Reply!.MessageType);
        Assert.Equal(s_served, outcome.Reply.NextServerAddress);
    }

    // The machine with a pending restart, the machine with a run: the server finds both by what the datagram carries.
    [Fact]
    public void AsksForTheMachineByItsHardwareAddressAndItsUuid()
    {
        Assert.True(DhcpMessageParser.TryParse(Loopback.Fixture("Dhcp", "discover-uefi-x64-pxeclient"), out DhcpMessage? message, out _));

        Discover(Handler());

        NetbootMachine asked = Assert.Single(_choice.Asked);
        Assert.Equal(Convert.ToHexString(message.ClientHardwareAddress.Span), asked.Mac);
        Assert.Equal(12, asked.Mac.Length);
        Assert.Equal(message.MachineIdentifier.Length == 17 ? message.MachineIdentifier[1..].ToArray() : [], asked.Uuid.ToArray());
    }

    [Theory]
    [InlineData(true, WindowsPE)]
    [InlineData(false, WindowsPE)]
    public void AMachineWhoseFirmwareRefusesTheShimIsNamedTheWindowsBootFileWhateverTheDefault(bool built, string expected)
    {
        if (built)
        {
            WindowsPEIsBuilt();
        }

        LinuxIsThere();
        _choice.Wish = new BootWish(null, LinuxRefused: true);

        Assert.Equal(expected, Named(Discover(Handler("Linux"))));
        Assert.Equal(expected, Named(Discover(Handler("Auto"))));
    }

    [Fact]
    public void ALinuxRunOnAMachineWhoseFirmwareRefusesTheShimStartsWindowsPEWhichSaysWhy()
    {
        WindowsPEIsBuilt();
        LinuxIsThere();
        _choice.Wish = new BootWish(AgentEnvironment.LinuxBoot, LinuxRefused: true);

        Assert.Equal(WindowsPE, Named(Discover(Handler())));
    }

    // Neither can be started for it, so the firmware goes on to its next boot entry instead of a file that is refused.
    [Theory]
    [InlineData(AgentEnvironment.LinuxBoot, true, true)]
    [InlineData(AgentEnvironment.LinuxBoot, false, false)]
    [InlineData(AgentEnvironment.WindowsPE, false, false)]
    [InlineData(AgentEnvironment.WindowsPE, true, true)]
    public void AMachineWhoseEnvironmentCannotBeStartedNorTheOtherGetsNoAnswer(AgentEnvironment wanted, bool bundle, bool refused)
    {
        if (bundle)
        {
            LinuxIsThere();
        }

        _choice.Wish = new BootWish(wanted, refused);

        ProxyDhcpOutcome outcome = Discover(Handler());

        Assert.Equal(ProxyDhcpOutcomeKind.Silenced, outcome.Kind);
        Assert.Equal(ProxyDhcpSilenceReason.NoBootEnvironmentForMachine, outcome.SilenceReason);
    }

    [Fact]
    public void TheRequestOnTheBootServerPortGetsTheSameChoiceAsTheOffer()
    {
        LinuxIsThere();
        WindowsPEIsBuilt();
        _choice.Wish = new BootWish(AgentEnvironment.LinuxBoot, false);
        IPEndPoint source = new(IPAddress.Parse("172.24.64.50"), 4011);

        ProxyDhcpOutcome outcome = Handler().Handle(
            Loopback.Fixture("Dhcp", "request-4011-uefi-x64"),
            ProxyDhcpListenPort.PxeBootServer,
            new DatagramArrival(ServedIndex, s_served, source),
            new byte[1500]);

        Assert.Equal(Shim, Named(outcome));
        Assert.Equal(DhcpMessageType.Ack, outcome.Reply!.MessageType);
        Assert.Equal(4011, outcome.Reply.Destination.Port);
        Assert.Equal(12, Assert.Single(_choice.Asked).Mac.Length);
    }

    [Fact]
    public void ARelayedRequestGetsTheChoiceOfTheMachineBehindTheRelay()
    {
        LinuxIsThere();
        WindowsPEIsBuilt();
        _choice.Wish = new BootWish(AgentEnvironment.LinuxBoot, false);
        byte[] relayed = Loopback.Fixture("Dhcp", "discover-uefi-x64-pxeclient");
        s_relay.GetAddressBytes().CopyTo(relayed, 24);

        ProxyDhcpOutcome outcome = Handler().Handle(
            relayed,
            ProxyDhcpListenPort.Dhcp,
            new DatagramArrival(ServedIndex, s_served, new IPEndPoint(s_relay, 67)),
            new byte[1500]);

        Assert.Equal(Shim, Named(outcome));
        Assert.Equal(new DhcpReplyDestination(s_relay, 67), outcome.Reply!.Destination);
    }

    // Every other reason to stay silent keeps its name, also for a machine nothing could be started for.
    [Fact]
    public void ARelayThatIsNotAuthorisedIsStillTheReasonForSilence()
    {
        _choice.Wish = new BootWish(AgentEnvironment.LinuxBoot, false);
        byte[] relayed = Loopback.Fixture("Dhcp", "discover-uefi-x64-pxeclient");
        IPAddress.Parse("192.0.2.9").GetAddressBytes().CopyTo(relayed, 24);

        ProxyDhcpOutcome outcome = Handler().Handle(
            relayed,
            ProxyDhcpListenPort.Dhcp,
            new DatagramArrival(ServedIndex, s_served, new IPEndPoint(IPAddress.Parse("192.0.2.9"), 67)),
            new byte[1500]);

        Assert.Equal(ProxyDhcpSilenceReason.RelayNotAuthorised, outcome.SilenceReason);
    }

    // UEFI HTTP Boot firmware takes a URL and no TFTP path, and a BIOS could not start the shim.
    [Theory]
    [InlineData("discover-httpclient-x64", "http://172.24.64.1:8080/boot/x64/bootmgfw.efi")]
    [InlineData("discover-bios-x86", "bios/pxeboot.n12")]
    public void AMachineOfAnotherKindKeepsItsBootTargetAndIsNotLookedUp(string fixture, string expected)
    {
        LinuxIsThere();
        _choice.Wish = new BootWish(AgentEnvironment.LinuxBoot, false);

        Assert.Equal(expected, Named(Discover(Handler("Linux"), fixture)));
        Assert.Empty(_choice.Asked);
    }

    [Fact]
    public void AListThatFailsLeavesTheMachineWithTheDefault()
    {
        LinuxIsThere();
        WindowsPEIsBuilt();
        _choice.Failure = new InvalidOperationException("The list is being rebuilt.");

        Assert.Equal(WindowsPE, Named(Discover(Handler("Auto"))));
        Assert.Equal(Shim, Named(Discover(Handler("Linux"))));
    }

    // The shim is loaded over TFTP, so a server that serves none cannot start Linux.
    [Fact]
    public void WithoutTftpTheLinuxBootEnvironmentIsNotOffered()
    {
        LinuxIsThere();
        _choice.Wish = new BootWish(AgentEnvironment.LinuxBoot, false);

        ProxyDhcpOutcome outcome = Discover(Handler("Linux", options =>
        {
            options.EnableTftp = false;
            options.BootTargets["X64Uefi"].ServerAddress = "172.24.64.9";
            options.BootTargets["X86Bios"].ServerAddress = "172.24.64.9";
        }));

        Assert.Equal(WindowsPE, Named(outcome));
        Assert.Equal(IPAddress.Parse("172.24.64.9"), outcome.Reply!.NextServerAddress);
    }

    // A boot target on another TFTP server cannot be looked into, so it counts as there.
    [Fact]
    public void ABootTargetOnAnotherServerCountsAsWindowsPEThatCanBeStarted()
    {
        LinuxIsThere();

        ProxyDhcpOutcome outcome = Discover(Handler("Auto", options => options.BootTargets["X64Uefi"].ServerAddress = "172.24.64.9"));

        Assert.Equal(WindowsPE, Named(outcome));
    }

    [Fact]
    public void WithoutABootTargetOfItsOwnTheMachineStillGetsLinux()
    {
        LinuxIsThere();

        ProxyDhcpOutcome outcome = Discover(Handler("Auto", options => options.BootTargets.Remove("X64Uefi")));

        Assert.Equal(Shim, Named(outcome));
    }

    // Every PXE request on the segment asks, so the files are looked at once in five seconds and not per datagram.
    [Fact]
    public void LooksAtTheFilesAgainOnlyAfterFiveSeconds()
    {
        LinuxIsThere();
        ProxyDhcpHandler handler = Handler();
        Assert.Equal(Shim, Named(Discover(handler)));

        WindowsPEIsBuilt();
        _clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(Shim, Named(Discover(handler)));

        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(WindowsPE, Named(Discover(handler)));
    }
}
