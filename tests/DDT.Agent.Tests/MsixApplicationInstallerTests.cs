// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Applications;
using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Contracts.Applications;
using Xunit;
using static DDT.Agent.Tests.TestApplications;

namespace DDT.Agent.Tests;

public sealed class MsixApplicationInstallerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ddt-msix-").FullName;
    private readonly RecordingToolRunner _tools = new();
    private readonly AgentLog _log = new(TimeProvider.System, TextWriter.Null);

    private string Files => Path.Combine(_root, "packages", "files");

    private string Logs => Path.Combine(_root, "logs");

    private string LogFile => Path.Combine(Logs, $"dism-{Tool.Id:D}.log");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private Task<int> InstallAsync() =>
        new MsixApplicationInstaller(_tools, _log).InstallAsync(
            new ApplicationLaunch(Tool with { Kind = ApplicationKind.Msix, Command = null }, Files, Path.Combine(_root, "scripts"), Logs, new Dictionary<string, string>()),
            TestContext.Current.CancellationToken);

    private string At(string relative) => Path.Combine(Files, relative);

    [Fact]
    public async Task ProvisionsOnePackageWithoutALicenceAsSystemWithTheApplicationsTimeout()
    {
        TestMsix.WritePackage(At("Contoso.App.msix"), "Contoso.App");
        _tools.AnswerExitCode = (_, _, _) => 3010;

        int exitCode = await InstallAsync();

        Assert.Equal(3010, exitCode);
        Assert.Equal(
            [RecordingToolRunner.CommandLine(
                InjectDriversStepRunner.DismPath,
                "/Online",
                "/Add-ProvisionedAppxPackage",
                $"/PackagePath:{At("Contoso.App.msix")}",
                "/SkipLicense",
                $"/LogPath:{LogFile}")],
            _tools.Calls);
        ToolRunOptions options = Assert.Single(_tools.Options);
        Assert.Equal((Files, TimeSpan.FromMinutes(5), (IAccountSession?)null), (options.WorkingDirectory, options.Timeout, options.Account));
    }

    [Fact]
    public async Task ProvisionsABundleWithItsFrameworksAndItsLicence()
    {
        TestMsix.WriteBundle(At("Contoso.App.msixbundle"), "Contoso.App");
        TestMsix.WritePackage(At(@"Dependencies\x64\Microsoft.VCLibs.x64.appx"), "Microsoft.VCLibs.140.00", framework: true);
        TestMsix.WritePackage(At(@"Dependencies\Microsoft.UI.Xaml.appx"), "Microsoft.UI.Xaml.2.8", framework: true);
        TestMsix.WriteText(At("Contoso.App_License1.xml"), TestMsix.License);

        await InstallAsync();

        Assert.Equal(
            [RecordingToolRunner.CommandLine(
                InjectDriversStepRunner.DismPath,
                "/Online",
                "/Add-ProvisionedAppxPackage",
                $"/PackagePath:{At("Contoso.App.msixbundle")}",
                $"/DependencyPackagePath:{At(@"Dependencies\Microsoft.UI.Xaml.appx")}",
                $"/DependencyPackagePath:{At(@"Dependencies\x64\Microsoft.VCLibs.x64.appx")}",
                $"/LicensePath:{At("Contoso.App_License1.xml")}",
                $"/LogPath:{LogFile}")],
            _tools.Calls);
    }

    [Fact]
    public async Task ReturnsWhatDismEndedWith()
    {
        TestMsix.WritePackage(At("Contoso.App.msix"), "Contoso.App");
        _tools.AnswerExitCode = (_, _, _) => unchecked((int)0x80073CF3);

        Assert.Equal(unchecked((int)0x80073CF3), await InstallAsync());
    }

    [Fact]
    public async Task FilesDismCannotTakeRunNothing()
    {
        TestMsix.WritePackage(At(@"a\Contoso.App.msix"), "Contoso.App");
        TestMsix.WritePackage(At(@"b\Fabrikam.Tool.msix"), "Fabrikam.Tool");

        DeploymentStepException refused = await Assert.ThrowsAsync<DeploymentStepException>(InstallAsync);

        Assert.Equal("The files hold more than one main package: a/Contoso.App.msix, b/Fabrikam.Tool.msix. Keep one.", refused.Message);
        Assert.Empty(_tools.Calls);
    }

    [Fact]
    public async Task AnApplicationWithoutFilesHasNoPackage()
    {
        DeploymentStepException refused = await Assert.ThrowsAsync<DeploymentStepException>(InstallAsync);

        Assert.Equal("The files hold no MSIX or Appx package.", refused.Message);
    }
}
