// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Core.Configuration;
using DDT.Protocols.Dhcp;
using DDT.Protocols.Pxe;
using Xunit;

namespace DDT.Pxe.Tests;

// What the pxe settings say about the Linux boot environment: the default, the kernel's extra words, and the second
// set of answers ProxyDHCP gives with the shim.
public sealed class PxeSetupLinuxTests
{
    [Theory]
    [InlineData("Auto", DefaultBootEnvironment.Auto)]
    [InlineData("auto", DefaultBootEnvironment.Auto)]
    [InlineData("WindowsPE", DefaultBootEnvironment.WindowsPE)]
    [InlineData("windowspe", DefaultBootEnvironment.WindowsPE)]
    [InlineData(" Linux ", DefaultBootEnvironment.Linux)]
    public void ReadsTheDefaultBootEnvironmentByItsName(string value, DefaultBootEnvironment expected)
    {
        PxeOptions options = new() { DefaultBootEnvironment = value };

        Assert.Empty(PxeSetup.FindProblems(options));
        Assert.Equal(expected, PxeSetup.Create(options, Loopback.Map()).DefaultEnvironment);
    }

    [Fact]
    public void TheDefaultIsAutoSoAnUpgradedServerKeepsStartingWindowsPE()
    {
        Assert.Equal("Auto", new PxeOptions().DefaultBootEnvironment);
        Assert.Equal(DefaultBootEnvironment.Auto, PxeSetup.Create(new PxeOptions(), Loopback.Map()).DefaultEnvironment);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("LinuxBoot")]
    [InlineData("Windows")]
    [InlineData("Auto,Linux")]
    public void RefusesADefaultBootEnvironmentThatIsNone(string value)
    {
        SettingProblem problem = Assert.Single(PxeSetup.FindProblems(new PxeOptions { DefaultBootEnvironment = value }));

        Assert.Equal("DefaultBootEnvironment", problem.Field);
        Assert.Equal($"'{value}' is not a boot environment. Use Auto, WindowsPE or Linux.", problem.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("console=ttyS0,115200")]
    [InlineData("console=ttyS0,115200 ddt.shell  module_blacklist=nouveau")]
    public void AcceptsKernelArgumentsOfAllowedWords(string arguments)
    {
        Assert.Empty(PxeSetup.FindProblems(new PxeOptions { LinuxKernelArguments = arguments }));
    }

    [Theory]
    [InlineData("quiet; reboot", "'quiet;' cannot be added to the kernel command line.")]
    [InlineData("a=$(halt)", "'a=$(halt)' cannot be added to the kernel command line.")]
    [InlineData("quiet -- x", "'--' cannot be added to the kernel command line.")]
    [InlineData("quiet \"x\"", "'\"x\"' cannot be added to the kernel command line.")]
    [InlineData("quiet rdinit=/bin/sh", "'rdinit=/bin/sh' would replace what DDT sets itself.")]
    [InlineData("init=/bin/sh", "'init=/bin/sh' would replace what DDT sets itself.")]
    [InlineData("ddt.hostip=10.0.0.9", "'ddt.hostip=10.0.0.9' would replace what DDT sets itself.")]
    public void RefusesKernelArgumentsByTheirFirstBadWord(string arguments, string expected)
    {
        SettingProblem problem = Assert.Single(PxeSetup.FindProblems(new PxeOptions { LinuxKernelArguments = arguments }));

        Assert.Equal("LinuxKernelArguments", problem.Field);
        Assert.StartsWith(expected, problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesKernelArgumentsLongerThanTheLimit()
    {
        PxeOptions options = new() { LinuxKernelArguments = new string('a', KernelArguments.MaxLength + 1) };

        SettingProblem problem = Assert.Single(PxeSetup.FindProblems(options));

        Assert.Equal("LinuxKernelArguments", problem.Field);
        Assert.Equal("Use at most 256 characters.", problem.Message);
    }

    // The same answers as for Windows PE, except that 64-bit UEFI machines are named the shim on this server's TFTP.
    [Fact]
    public void TheLinuxAnswersNameTheShimForX64UefiAndKeepEveryOtherTarget()
    {
        PxeOptions options = new()
        {
            AuthorisedRelayAgents = "192.0.2.1",
            BootTargets =
            {
                ["X64Uefi"] = new BootTargetOptions { Method = "Tftp", BootFile = "x64/bootmgfw.efi", ServerHostName = "wds" },
                ["X64UefiHttp"] = new BootTargetOptions { Method = "Http", BootFile = "http://192.0.2.10:8080/boot/x64/bootmgfw.efi" },
            },
        };

        PxeSetup setup = PxeSetup.Create(options, Loopback.Map());
        ProxyDhcpConfiguration linux = setup.LinuxProxyDhcp!;

        BootTarget shim = linux.BootTargets[ClientArchitecture.X64Uefi];
        Assert.Equal("linux/shimx64.efi", shim.BootFile);
        Assert.Equal(PxeSetup.LinuxBootFile, shim.BootFile);
        Assert.Equal(BootMethod.Tftp, shim.Method);
        Assert.Null(shim.ServerAddress);
        Assert.Null(shim.ServerHostName);
        Assert.Same(setup.ProxyDhcp.BootTargets[ClientArchitecture.X64UefiHttp], linux.BootTargets[ClientArchitecture.X64UefiHttp]);
        Assert.Equal(setup.ProxyDhcp.AuthorisedRelayAgents, linux.AuthorisedRelayAgents);
        Assert.Equal(setup.ProxyDhcp.LocalAddresses, linux.LocalAddresses);
        Assert.Equal("x64/bootmgfw.efi", setup.ProxyDhcp.BootTargets[ClientArchitecture.X64Uefi].BootFile);
    }

    [Fact]
    public void WithoutTftpThereAreNoLinuxAnswers()
    {
        Assert.Null(PxeSetup.Create(new PxeOptions { EnableTftp = false }, Loopback.Map()).LinuxProxyDhcp);
        Assert.NotNull(PxeSetup.Create(new PxeOptions(), Loopback.Map()).LinuxProxyDhcp);
    }
}
