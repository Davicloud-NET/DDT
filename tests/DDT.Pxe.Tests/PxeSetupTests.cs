// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using DDT.Core.Configuration;
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
    [InlineData("X64Uefl", "Tftp", "x64/bootmgfw.efi", "BootTargets:X64Uefl", "is not a client architecture")]
    [InlineData("7", "Tftp", "x64/bootmgfw.efi", "BootTargets:7", "is not a client architecture")]
    [InlineData("X64Uefi", null, "x64/bootmgfw.efi", "BootTargets:X64Uefi:Method", "Must be Tftp or Http")]
    [InlineData("X64Uefi", "Http", "http://192.0.2.10/boot/x64/bootmgfw.efi", "BootTargets:X64Uefi:Method", "Must be Tftp for X64Uefi")]
    [InlineData("X64UefiHttp", "Tftp", "x64/bootmgfw.efi", "BootTargets:X64UefiHttp:Method", "Must be Http for X64UefiHttp")]
    [InlineData("X64UefiHttp", "Http", "x64/bootmgfw.efi", "BootTargets:X64UefiHttp:BootFile", "absolute http or https URL")]
    [InlineData("X64Uefi", "Tftp", null, "BootTargets:X64Uefi:BootFile", "Must be set")]
    public void RefusesABootTargetByName(string key, string? method, string? bootFile, string field, string expected)
    {
        PxeOptions options = new()
        {
            BootTargets = { [key] = new BootTargetOptions { Method = method, BootFile = bootFile } },
        };

        SettingProblem problem = Assert.Single(PxeSetup.FindProblems(options));

        Assert.Equal(field, problem.Field);
        Assert.Contains(expected, problem.Message, StringComparison.Ordinal);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => PxeSetup.Create(options, Loopback.Map()));

        Assert.Contains($"DDT:Pxe:{field}: ", exception.Message, StringComparison.Ordinal);
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

        Assert.Equal("AuthorisedRelayAgents", Assert.Single(PxeSetup.FindProblems(options)).Field);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65)]
    public void RefusesAWindowSizeOutOfRange(int windowSize)
    {
        PxeOptions options = new() { TftpMaxWindowSize = windowSize };

        Assert.Equal("TftpMaxWindowSize", Assert.Single(PxeSetup.FindProblems(options)).Field);
    }

    [Fact]
    public void RefusesATftpTargetServedByDdtWhenTftpIsOff()
    {
        PxeOptions options = new()
        {
            EnableTftp = false,
            BootTargets = { ["X64Uefi"] = new BootTargetOptions { Method = "Tftp", BootFile = "x64/bootmgfw.efi" } },
        };

        SettingProblem problem = Assert.Single(PxeSetup.FindProblems(options));

        Assert.Equal("BootTargets:X64Uefi:ServerAddress", problem.Field);
        Assert.StartsWith("Required while EnableTftp is false", problem.Message, StringComparison.Ordinal);
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
    public void WithoutABootTargetX64UefiMachinesGetTheBootManagerOverTftp()
    {
        PxeSetup setup = PxeSetup.Create(new PxeOptions(), Loopback.Map());

        BootTarget target = Assert.Single(setup.ProxyDhcp.BootTargets).Value;
        Assert.Equal(ClientArchitecture.X64Uefi, target.Architecture);
        Assert.Equal(BootMethod.Tftp, target.Method);
        Assert.Equal("x64/bootmgfw.efi", target.BootFile);
        Assert.Null(target.ServerAddress);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void TheDefaultBootTargetNeedsProxyDhcpAndTftp(bool proxyDhcp, bool tftp)
    {
        PxeOptions options = new() { EnableProxyDhcp = proxyDhcp, EnableTftp = tftp };

        Assert.Empty(PxeSetup.FindProblems(options));
        Assert.Empty(PxeSetup.Create(options, Loopback.Map()).ProxyDhcp.BootTargets);
    }

    [Fact]
    public void AConfiguredBootTargetLeavesTheDefaultOut()
    {
        PxeOptions options = new()
        {
            BootTargets = { ["X64UefiHttp"] = new BootTargetOptions { Method = "Http", BootFile = "http://192.0.2.10:8080/boot/x64/bootmgfw.efi" } },
        };

        Assert.Equal([ClientArchitecture.X64UefiHttp], PxeSetup.Create(options, Loopback.Map()).ProxyDhcp.BootTargets.Keys);
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

        Assert.Equal(
            ["BootTargets:Nope", "AuthorisedRelayAgents", "HttpBootPort"],
            PxeSetup.FindProblems(options).Select(problem => problem.Field));
    }
}
