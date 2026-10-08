// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using DDT.Contracts.Agents;
using DDT.Contracts.BootImage;
using DDT.Contracts.Deployments;
using DDT.Contracts.Images;
using DDT.Contracts.Messages;
using DDT.Contracts.Server;
using DDT.Contracts.Settings;
using DDT.Pxe;
using DDT.Server.Certificates;
using DDT.Server.LinuxBoot;
using DDT.Server.Machines;
using DDT.Server.Settings;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DDT.Server.Tests;

// What the pages, the settings and netboot are told about the Linux boot environment of a server that has one.
public sealed class LinuxBootPagesTests(BothEnvironmentsApplication application) : IClassFixture<BothEnvironmentsApplication>
{
    private async Task<BootImageView> BootImageAsync() =>
        await RegisteredMachine.ReadAsync<BootImageView>(await (await application.AdministratorAsync()).GetAsync("/api/boot-image"));

    [Fact]
    public async Task TheBootImagePageShowsWhatTheReleaseBrought()
    {
        application.SetLinuxBoot(true);

        LinuxBootView linux = (await BootImageAsync()).Linux!;

        Assert.True(linux.Available);
        Assert.Null(linux.Problem);
        Assert.Null(linux.ProblemText);
        Assert.Equal("20261006T203214Z", linux.Snapshot);
        Assert.Equal("7.2.6+deb13-amd64", linux.Kernel);
        Assert.Equal("1.51~1+deb13u1+16.1-2~deb13u1", linux.Shim);
        Assert.Equal("1+2.12+9+deb13u2", linux.Grub);
        Assert.Equal(TestLinuxBoot.Keymaps, linux.Keymaps);
        Assert.Equal("linux/shimx64.efi", linux.BootFile);
    }

    [Fact]
    public async Task TheBootImagePageSaysWhyTheLinuxBootEnvironmentCannotBeStarted()
    {
        application.SetLinuxBoot(false);

        try
        {
            LinuxBootView linux = (await BootImageAsync()).Linux!;

            Assert.False(linux.Available);
            Assert.Equal(ServerMessages.LinuxBootMissing.Code, linux.ProblemText?.Code);
            Assert.Equal(
                $"This server has no Linux boot environment: {application.LinuxBootFolder} holds no ddt-linux-boot.json. A release of DDT brings the folder with it.",
                linux.Problem);
            Assert.Empty(linux.Keymaps);
        }
        finally
        {
            application.SetLinuxBoot(true);
        }
    }

    // The Linux boot environment needs no build, so it is ready on a server that never built a boot image.
    [Fact]
    public async Task TheChecklistCountsTheLinuxBootEnvironmentBesideTheBootImage()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        application.SetBootImage(false);

        try
        {
            SetupChecklist without = await RegisteredMachine.ReadAsync<SetupChecklist>(await administrator.GetAsync("/api/server/checklist"));
            application.SetLinuxBoot(false);
            SetupChecklist neither = await RegisteredMachine.ReadAsync<SetupChecklist>(await administrator.GetAsync("/api/server/checklist"));

            Assert.Equal((false, true), (without.BootImage, without.LinuxBoot));
            Assert.Equal((false, false), (neither.BootImage, neither.LinuxBoot));
        }
        finally
        {
            application.SetBootImage(true);
            application.SetLinuxBoot(true);
        }
    }

    [Fact]
    public async Task TheAssignDialogLearnsWhichAuthoritiesTheShimIsSignedUnder()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        TestLinuxBoot.Write(application.LinuxBootFolder, TestPe.Fixture("shimx64.efi.dualsigned"));

        try
        {
            DeploymentOptionsView options = await RegisteredMachine.ReadAsync<DeploymentOptionsView>(await administrator.GetAsync("/api/deployments/options"));

            Assert.Equal(UefiCa.Microsoft2011, options.LinuxShimSignedUnder);
            Assert.Equal(UefiCa.Microsoft2011, (await BootImageAsync()).Linux!.ShimSignedUnder);
        }
        finally
        {
            application.SetLinuxBoot(true);
        }
    }

    [Fact]
    public async Task NetbootIsHandedTheFilesTheServersAddressItsRootAndTheSettings()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        LinuxBootSite site = application.Services.GetRequiredService<LinuxBootSite>();
        application.SetLinuxBoot(true);
        site.Refresh();

        LinuxBootBundle plain = site.Current!;

        Assert.Equal(application.LinuxBootFolder, plain.Folder);
        Assert.Equal(TestLinuxBoot.Names.Order(), plain.Files.Keys.Order());
        Assert.All(TestLinuxBoot.Names, name => Assert.Equal(new FileInfo(Path.Combine(plain.Folder, name)).Length, plain.Files[name].Length));
        Assert.Equal(new Uri(application.Services.GetRequiredService<ServerAddress>().Url), plain.ServerUrl);
        Assert.Equal(Uri.UriSchemeHttps, plain.ServerUrl.Scheme);
        Assert.Equal(TestLinuxBoot.Root, plain.RootCertificatePem);

        await administrator.SavedAsync<DeploymentSettings>(SettingsSectionNames.Deployment, values => values with { ConsoleKeyboardLayout = "de" });
        await administrator.SavedAsync<PxeSettings>(SettingsSectionNames.Pxe, values => values with { LinuxKernelArguments = "console=ttyS0,115200" });

        try
        {
            LinuxBootBundle set = site.Current!;

            Assert.Equal("de", set.Keymap);
            Assert.Equal("German (de)", set.KeyboardLayout);
            Assert.Equal("console=ttyS0,115200", set.KernelArguments);
        }
        finally
        {
            await administrator.SavedAsync<DeploymentSettings>(SettingsSectionNames.Deployment, values => values with { ConsoleKeyboardLayout = null });
            await administrator.SavedAsync<PxeSettings>(SettingsSectionNames.Pxe, values => values with { LinuxKernelArguments = null });
        }

        Assert.Equal("us", site.Current!.Keymap);
        Assert.Equal("English (United States) (us)", site.Current.KeyboardLayout);
        Assert.Equal(string.Empty, site.Current.KernelArguments);
    }

    // Netboot asks from the loops that receive datagrams, so the answer is what the last look found. Netboot itself
    // has the files looked at again every few seconds.
    [Fact]
    public void NetbootIsHandedNothingOnceALookFoundThatTheEnvironmentCannotBeStarted()
    {
        LinuxBootSite site = application.Services.GetRequiredService<LinuxBootSite>();
        application.SetLinuxBoot(true);
        site.Refresh();
        application.SetLinuxBoot(false);

        try
        {
            Assert.NotNull(site.Current);

            site.Refresh();

            Assert.Null(site.Current);
        }
        finally
        {
            application.SetLinuxBoot(true);
            site.Refresh();
        }

        Assert.NotNull(site.Current);
    }

    // What netboot finds when it opens a file reaches the catalogue through the bundle it was handed.
    [Fact]
    public void AFileNetbootFindsChangedTakesTheEnvironmentAwayUntilItWasReadAgain()
    {
        LinuxBootSite site = application.Services.GetRequiredService<LinuxBootSite>();
        application.SetLinuxBoot(true);
        site.Refresh();

        try
        {
            site.Current!.Changed!("vmlinuz");

            Assert.Null(site.Current);
            Assert.False(application.Services.GetRequiredService<BootEnvironments>().Exists(AgentEnvironment.LinuxBoot));
        }
        finally
        {
            site.Refresh();
        }

        // The files are what the description names, as the look that read them again found.
        Assert.NotNull(site.Current);
    }

    [Theory]
    [InlineData("de", "de")]
    [InlineData(" fr ", "fr")]
    [InlineData("pl", "us")]
    [InlineData("DE", "us")]
    [InlineData("", "us")]
    [InlineData(null, "us")]
    public void ALayoutTheEnvironmentLacksBecomesItsDefault(string? configured, string expected)
    {
        Assert.Equal(expected, LinuxKeyboardLayouts.Keymap(configured, TestLinuxBoot.Keymaps));
        Assert.Equal("us", LinuxKeyboardLayouts.Keymap(configured, []));
    }

    [Fact]
    public async Task TheKeyboardLayoutIsOneTheEnvironmentHas()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        application.SetLinuxBoot(true);
        SettingsSectionView<DeploymentSettings> current = await administrator.SectionAsync<DeploymentSettings>(SettingsSectionNames.Deployment);

        using HttpResponseMessage unknown = await administrator.SaveAsync(SettingsSectionNames.Deployment, current.Version, current.Values with { ConsoleKeyboardLayout = "pl" });
        using HttpResponseMessage malformed = await administrator.SaveAsync(SettingsSectionNames.Deployment, current.Version, current.Values with { ConsoleKeyboardLayout = "de; reboot" });

        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal(
            ["The Linux boot environment of this server has no keyboard layout 'pl'. It has us, uk, de, ch, fr, it, es."],
            (await SettingsRequests.ProblemsAsync(unknown)).Errors["consoleKeyboardLayout"]);
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.StartsWith(
            "'de; reboot' is not the name of a keyboard layout.",
            Assert.Single((await SettingsRequests.ProblemsAsync(malformed)).Errors["consoleKeyboardLayout"]),
            StringComparison.Ordinal);
    }

    // The layout is for the console in the Linux boot environment. Windows PE has its own from the boot image's build.
    [Fact]
    public async Task ARegistrationFromTheLinuxBootEnvironmentIsToldTheKeyboardLayout()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        application.SetLinuxBoot(true);
        await administrator.SavedAsync<DeploymentSettings>(SettingsSectionNames.Deployment, values => values with { ConsoleKeyboardLayout = "ch" });

        try
        {
            using DeployingMachine linux = await DeployingMachine.RegisterAsync(application, DeployingMachine.LinuxBoot());
            using DeployingMachine windowsPE = await DeployingMachine.RegisterAsync(application, registration => registration with { Environment = AgentEnvironment.WindowsPE });

            Assert.Equal("ch", (await linux.RegisterAgainAsync()).ConsoleKeyboardLayout);
            Assert.Null((await windowsPE.RegisterAgainAsync()).ConsoleKeyboardLayout);
        }
        finally
        {
            await administrator.SavedAsync<DeploymentSettings>(SettingsSectionNames.Deployment, values => values with { ConsoleKeyboardLayout = null });
        }
    }

    // Netboot then names the Windows PE boot file, which the Network boot page would otherwise not say.
    [Fact]
    public async Task ThePageWarnsWhenLinuxIsTheDefaultAndCannotBeStarted()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        application.SetLinuxBoot(true);
        SettingsSectionView<PxeSettings> saved = await administrator.SavedAsync<PxeSettings>(SettingsSectionNames.Pxe, values => values with { DefaultBootEnvironment = "Linux" });

        try
        {
            Assert.DoesNotContain(saved.Warnings, warning => warning.Code == SettingWarningCodes.PxeLinuxDefaultUnavailable);

            application.SetLinuxBoot(false);
            SettingMessage warning = Assert.Single(
                (await administrator.SectionAsync<PxeSettings>(SettingsSectionNames.Pxe)).Warnings,
                candidate => candidate.Code == SettingWarningCodes.PxeLinuxDefaultUnavailable);

            Assert.Equal("defaultBootEnvironment", warning.Field);
            Assert.Equal(
                "Linux is the default, but this server cannot start the Linux boot environment, so machines without a run are named the Windows PE boot file. "
                + $"This server has no Linux boot environment: {application.LinuxBootFolder} holds no ddt-linux-boot.json. A release of DDT brings the folder with it.",
                warning.Message);
            Assert.Equal(ServerMessages.SettingsPxeLinuxDefaultUnavailable.Code, warning.Text?.Code);

            // It only informs: the section can still be saved.
            Assert.False(SettingWarningCodes.NeedConfirmation.Contains(SettingWarningCodes.PxeLinuxDefaultUnavailable));
        }
        finally
        {
            application.SetLinuxBoot(true);
            await administrator.SavedAsync<PxeSettings>(SettingsSectionNames.Pxe, values => values with { DefaultBootEnvironment = "Auto" });
        }
    }

    [Fact]
    public async Task AnotherDefaultThanLinuxGetsNoWarningWithoutTheEnvironment()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        application.SetLinuxBoot(false);

        try
        {
            SettingsSectionView<PxeSettings> view = await administrator.SectionAsync<PxeSettings>(SettingsSectionNames.Pxe);

            Assert.DoesNotContain(view.Warnings, warning => warning.Code == SettingWarningCodes.PxeLinuxDefaultUnavailable);
        }
        finally
        {
            application.SetLinuxBoot(true);
        }
    }
}
