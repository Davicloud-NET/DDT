using System.Net;
using DDT.Protocols.Dhcp;
using DDT.Protocols.Pxe;
using Xunit;

namespace DDT.Pxe.Tests;

public sealed class PxeSetupTests
{
    [Fact]
    public void BuildsTheProtocolConfigurationFromValidOptions()
    {
        PxeOptions options = new()
        {
            AuthorisedRelayAgents = "192.0.2.1, 198.51.100.1",
            TftpMaxWindowSize = 16,
            BootTargets =
            {
                ["x64uefi"] = new BootTargetOptions { Method = "tftp", BootFile = "x64/bootmgfw.efi" },
                ["X64UefiHttp"] = new BootTargetOptions { Method = "Http", BootFile = "http://192.0.2.10:8080/boot/x64/bootmgfw.efi" },
            },
        };

        PxeSetup setup = PxeSetup.Create(options, Loopback.Map());

        BootTarget target = setup.ProxyDhcp.BootTargets[ClientArchitecture.X64Uefi];
        Assert.Equal(BootMethod.Tftp, target.Method);
        Assert.Equal(BootMethod.Http, setup.ProxyDhcp.BootTargets[ClientArchitecture.X64UefiHttp].Method);
        Assert.Equal([IPAddress.Parse("192.0.2.1"), IPAddress.Parse("198.51.100.1")], setup.ProxyDhcp.AuthorisedRelayAgents);
        Assert.Equal([IPAddress.Loopback], setup.ProxyDhcp.LocalAddresses);
        Assert.Equal(16, setup.TftpLimits.MaxWindowSize);
        Assert.Equal(1380, setup.TftpLimits.MaxBlockSize);
    }

    [Theory]
    [InlineData("X64Uefl", "Tftp", "x64/bootmgfw.efi", "is not a client architecture")]
    [InlineData("7", "Tftp", "x64/bootmgfw.efi", "is not a client architecture")]
    [InlineData("X64Uefi", null, "x64/bootmgfw.efi", "Method must be Tftp or Http")]
    [InlineData("X64Uefi", "Http", "http://192.0.2.10/boot/x64/bootmgfw.efi", "Method must be Tftp for X64Uefi")]
    [InlineData("X64UefiHttp", "Tftp", "x64/bootmgfw.efi", "Method must be Http for X64UefiHttp")]
    [InlineData("X64UefiHttp", "Http", "x64/bootmgfw.efi", "absolute http or https URL")]
    [InlineData("X64Uefi", "Tftp", null, "BootFile must be set")]
    public void RefusesABootTargetByName(string key, string? method, string? bootFile, string expected)
    {
        PxeOptions options = new()
        {
            BootTargets = { [key] = new BootTargetOptions { Method = method, BootFile = bootFile } },
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => PxeSetup.Create(options, Loopback.Map()));

        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("10")]
    [InlineData("::1")]
    [InlineData("relay.example")]
    [InlineData("010.001.001.001")]
    [InlineData("192.168.001.010")]
    [InlineData("0x0A.0.0.1")]
    public void RefusesARelayAgentThatIsNotAnIpv4Address(string relay)
    {
        PxeOptions options = new() { AuthorisedRelayAgents = relay };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => PxeSetup.Create(options, Loopback.Map()));

        Assert.Contains("AuthorisedRelayAgents", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65)]
    public void RefusesAWindowSizeOutOfRange(int windowSize)
    {
        PxeOptions options = new() { TftpMaxWindowSize = windowSize };

        Assert.Throws<InvalidOperationException>(() => PxeSetup.Create(options, Loopback.Map()));
    }

    [Fact]
    public void RefusesATftpTargetServedByDdtWhenTftpIsOff()
    {
        PxeOptions options = new()
        {
            EnableTftp = false,
            BootTargets = { ["X64Uefi"] = new BootTargetOptions { Method = "Tftp", BootFile = "x64/bootmgfw.efi" } },
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => PxeSetup.Create(options, Loopback.Map()));

        Assert.Contains("BootTargets:X64Uefi uses Tftp while DDT:Pxe:EnableTftp is false", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AcceptsATftpTargetOnAnotherServerWhenTftpIsOff()
    {
        PxeOptions options = new()
        {
            EnableTftp = false,
            BootTargets =
            {
                ["X64Uefi"] = new BootTargetOptions { Method = "Tftp", BootFile = "x64/bootmgfw.efi", ServerAddress = "192.0.2.20" },
            },
        };

        PxeSetup setup = PxeSetup.Create(options, Loopback.Map());

        Assert.Equal(IPAddress.Parse("192.0.2.20"), setup.ProxyDhcp.BootTargets[ClientArchitecture.X64Uefi].ServerAddress);
    }

    [Fact]
    public void ReportsEveryFailureAtOnce()
    {
        PxeOptions options = new()
        {
            HttpBootPort = 0,
            AuthorisedRelayAgents = "nope",
            BootTargets = { ["Nope"] = new BootTargetOptions() },
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => PxeSetup.Create(options, Loopback.Map()));

        Assert.Contains("HttpBootPort", exception.Message, StringComparison.Ordinal);
        Assert.Contains("AuthorisedRelayAgents", exception.Message, StringComparison.Ordinal);
        Assert.Contains("BootTargets:Nope", exception.Message, StringComparison.Ordinal);
    }
}
