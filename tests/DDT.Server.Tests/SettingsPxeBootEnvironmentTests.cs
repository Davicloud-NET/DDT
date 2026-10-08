// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using DDT.Contracts.Settings;
using DDT.Pxe;
using DDT.Server.Settings;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DDT.Server.Tests;

// The pxe section's fields about the boot environments: the default, the kernel's extra words, and the DHCP port.
public sealed class SettingsPxeBootEnvironmentTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    [Fact]
    public void ANewServerStartsWithAutoNoExtraWordsAndItsOwnDhcpPort()
    {
        PxeOptions options = PxeSettingsSource.Desired(SettingsSnapshotTests.Build([])).Options!;
        PxeSettings values = SettingsApi.Pxe.Values(options);

        Assert.Equal("Auto", values.DefaultBootEnvironment);
        Assert.Equal(string.Empty, values.LinuxKernelArguments);
        Assert.False(values.LeaveDhcpPort);
    }

    [Fact]
    public async Task TheBootEnvironmentFieldsAreSavedAndReadBackAndReachTheListeners()
    {
        SignedInClient administrator = await application.AdministratorAsync();

        SettingsSectionView<PxeSettings> saved = await administrator.SavedAsync<PxeSettings>(
            SettingsSectionNames.Pxe,
            values => values with { DefaultBootEnvironment = "linux", LinuxKernelArguments = "  console=ttyS0,115200   ddt.shell ", LeaveDhcpPort = true });

        // The name as the code spells it, and one space between the words.
        Assert.Equal("Linux", saved.Values.DefaultBootEnvironment);
        Assert.Equal("console=ttyS0,115200 ddt.shell", saved.Values.LinuxKernelArguments);
        Assert.True(saved.Values.LeaveDhcpPort);

        PxeSettings read = (await administrator.SectionAsync<PxeSettings>(SettingsSectionNames.Pxe)).Values;
        Assert.Equal(saved.Values.DefaultBootEnvironment, read.DefaultBootEnvironment);
        Assert.Equal(saved.Values.LinuxKernelArguments, read.LinuxKernelArguments);

        PxeOptions served = PxeSettingsSource.Desired(application.Services.GetRequiredService<DdtSettings>().Current).Options!;
        Assert.Equal("Linux", served.DefaultBootEnvironment);
        Assert.Equal("console=ttyS0,115200 ddt.shell", served.LinuxKernelArguments);
        Assert.True(served.LeaveDhcpPort);
        Assert.Equal(saved.Version, PxeSettingsSource.Desired(application.Services.GetRequiredService<DdtSettings>().Current).Version);
    }

    [Theory]
    [InlineData("LinuxBoot", "'LinuxBoot' is not a boot environment. Use Auto, WindowsPE or Linux.")]
    [InlineData("2", "'2' is not a boot environment. Use Auto, WindowsPE or Linux.")]
    public async Task ADefaultThatIsNoBootEnvironmentIsRefusedAtItsField(string value, string expected)
    {
        SignedInClient administrator = await application.AdministratorAsync();
        SettingsSectionView<PxeSettings> current = await administrator.SectionAsync<PxeSettings>(SettingsSectionNames.Pxe);

        using HttpResponseMessage response = await administrator.SaveAsync(
            SettingsSectionNames.Pxe,
            current.Version,
            current.Values with { DefaultBootEnvironment = value });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        HttpValidationProblemDetails problems = await SettingsRequests.ProblemsAsync(response);
        Assert.Equal([expected], problems.Errors["defaultBootEnvironment"]);
    }

    // The words end up in the script GRUB runs on every machine that netboots Linux.
    [Theory]
    [InlineData("quiet; reboot", "'quiet;' cannot be added to the kernel command line.")]
    [InlineData("a=${prefix}", "'a=${prefix}' cannot be added to the kernel command line.")]
    [InlineData("quiet\nboot", null)]
    [InlineData("rdinit=/bin/sh", "'rdinit=/bin/sh' would replace what DDT sets itself.")]
    [InlineData("ddt.time=0", "'ddt.time=0' would replace what DDT sets itself.")]
    public async Task KernelArgumentsThatAreNotPlainWordsAreRefusedAtTheirField(string value, string? expected)
    {
        SignedInClient administrator = await application.AdministratorAsync();
        SettingsSectionView<PxeSettings> current = await administrator.SectionAsync<PxeSettings>(SettingsSectionNames.Pxe);

        using HttpResponseMessage response = await administrator.SaveAsync(
            SettingsSectionNames.Pxe,
            current.Version,
            current.Values with { LinuxKernelArguments = value });

        // A line break only separates two words, which are then two plain words.
        if (expected is null)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("quiet boot", (await RegisteredMachine.ReadAsync<SettingsSectionView<PxeSettings>>(response)).Values.LinuxKernelArguments);

            return;
        }

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        HttpValidationProblemDetails problems = await SettingsRequests.ProblemsAsync(response);
        Assert.StartsWith(expected, Assert.Single(problems.Errors["linuxKernelArguments"]), StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigurationSetsTheFieldsAndABadValueClosesTheSection()
    {
        PxeDesiredSetup configured = PxeSettingsSource.Desired(SettingsSnapshotTests.Build(
            [],
            ("DDT:Pxe:DefaultBootEnvironment", "WindowsPE"),
            ("DDT:Pxe:LinuxKernelArguments", "console=ttyS0,115200"),
            ("DDT:Pxe:LeaveDhcpPort", "true")));
        PxeDesiredSetup broken = PxeSettingsSource.Desired(SettingsSnapshotTests.Build([], ("DDT:Pxe:LinuxKernelArguments", "quiet $(reboot)")));

        Assert.Equal("WindowsPE", configured.Options!.DefaultBootEnvironment);
        Assert.Equal("console=ttyS0,115200", configured.Options.LinuxKernelArguments);
        Assert.True(configured.Options.LeaveDhcpPort);
        Assert.Null(broken.Options);
        Assert.Contains("DDT:Pxe:LinuxKernelArguments: '$(reboot)' cannot be added to the kernel command line.", broken.Refusal?.Text, StringComparison.Ordinal);
    }
}
