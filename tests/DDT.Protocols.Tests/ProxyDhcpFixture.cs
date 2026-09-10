using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Net;
using DDT.Protocols.Dhcp;
using DDT.Protocols.Pxe;

namespace DDT.Protocols.Tests;

internal static class ProxyDhcpFixture
{
    public static readonly IPAddress ServerAddress = IPAddress.Parse("192.0.2.10");
    public static readonly IPAddress RelayAddress = IPAddress.Parse("192.0.2.1");

    public static DhcpMessage Message(string fixture)
    {
        if (!DhcpMessageParser.TryParse(PacketFixture.Load("Dhcp", fixture), out DhcpMessage? message, out _))
        {
            throw new InvalidOperationException($"Fixture {fixture} does not parse.");
        }

        return message;
    }

    public static ProxyDhcpConfiguration Configuration(params BootTarget[] targets) =>
        new()
        {
            BootTargets = targets.ToFrozenDictionary(target => target.Architecture),
            LocalAddresses = [ServerAddress],
        };

    public static ProxyDhcpConfiguration Default() => Configuration(
        new BootTarget
        {
            Architecture = ClientArchitecture.X86Bios,
            Method = BootMethod.Tftp,
            BootFile = "ddt/x86/pxelinux.0",
            AdvertiseBootServerDiscovery = true,
        },
        new BootTarget
        {
            Architecture = ClientArchitecture.X64Uefi,
            Method = BootMethod.Tftp,
            BootFile = "ddt/x64/wdsmgfw.efi",
        },
        new BootTarget
        {
            Architecture = ClientArchitecture.X64UefiHttp,
            Method = BootMethod.Http,
            BootFile = "http://192.0.2.10:8080/boot/x64/bootmgfw.efi",
        });

    public static ProxyDhcpRequest Request(
        string fixture,
        ProxyDhcpListenPort port = ProxyDhcpListenPort.Dhcp,
        IPAddress? source = null,
        int sourcePort = 68) =>
        new()
        {
            Message = Message(fixture),
            ReceivedOn = port,
            LocalAddress = ServerAddress,
            SourceAddress = source ?? IPAddress.Any,
            SourcePort = sourcePort,
        };
}
