// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Applications;
using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Contracts.Agents;
using DDT.Contracts.Applications;
using Xunit;
using static DDT.Agent.Tests.TestApplications;

namespace DDT.Agent.Tests;

public sealed class MsixApplicationInstallerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ddt-msix-").FullName;
    // Contoso.App as TestMsix writes it, kept by Windows unless a test says otherwise.
    private const string Kept = "Contoso.App_1.0.0.0_x64__8wekyb3d8bbwe";

    private readonly RecordingToolRunner _tools = new();
    private readonly FakeDetectionSources _machine = new();
    private readonly ImmediateTimeProvider _time = new();
    private readonly StringWriter _output = new();
    private readonly AgentLog _log;

    public MsixApplicationInstallerTests()
    {
        _log = new AgentLog(TimeProvider.System, _output);
        _machine.Provisioned.Add(Kept);
    }

    private string Files => Path.Combine(_root, "packages", "files");

    private string Logs => Path.Combine(_root, "logs");

    private string LogFile => Path.Combine(Logs, $"dism-{Tool.Id:D}.log");

    public void Dispose()
    {
        _output.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    private Task<int> InstallAsync() => InstallAsync(Tool with { Kind = ApplicationKind.Msix, Command = null });

    private Task<int> InstallAsync(AgentApplication application) =>
        new MsixApplicationInstaller(_tools, _machine, _log, _time).InstallAsync(
            new ApplicationLaunch(application, Files, Path.Combine(_root, "scripts"), Logs, new Dictionary<string, string>()),
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
        Assert.Equal((Files, (IAccountSession?)null), (options.WorkingDirectory, options.Account));
        Assert.InRange(options.Timeout.GetValueOrDefault(), TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(5));
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

    [Fact]
    public async Task APackageWindowsKeepsAtOnceIsProvisionedOnce()
    {
        TestMsix.WritePackage(At("Contoso.App.msix"), "Contoso.App");

        Assert.Equal(0, await InstallAsync());

        Assert.Single(_tools.Calls);
        Assert.Equal([MsixApplicationInstaller.Settle], _time.Delays);
    }

    [Fact]
    public async Task APackageWindowsDropsOnceIsProvisionedAgain()
    {
        TestMsix.WritePackage(At("Contoso.App.msix"), "Contoso.App");
        _machine.Provisioned.Clear();
        _tools.AnswerExitCode = (_, _, _) =>
        {
            if (_tools.Calls.Count == 2)
            {
                _machine.Provisioned.Add(Kept);
            }

            return 0;
        };

        Assert.Equal(0, await InstallAsync());

        Assert.Equal(2, _tools.Calls.Count);
        Assert.Contains("Windows did not keep Contoso.App.msix, so it is provisioned again in 10 s.", _output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task APackageWindowsNeverKeepsFailsTheApplication()
    {
        TestMsix.WritePackage(At("Contoso.App.msix"), "Contoso.App");
        _machine.Provisioned.Clear();

        DeploymentStepException refused = await Assert.ThrowsAsync<DeploymentStepException>(InstallAsync);

        Assert.Equal("Windows did not keep Contoso.App.msix after 9 tries.", refused.Message);
        Assert.Equal(MsixApplicationInstaller.Retries + 1, _tools.Calls.Count);
    }

    // App Installer 1.21 of the image is no proof that 1.29 was kept.
    [Fact]
    public async Task AnOlderPackageOfTheFamilyIsNoProof()
    {
        TestMsix.WritePackage(At("Contoso.App.msix"), "Contoso.App");
        _machine.Provisioned.Clear();
        _machine.Provisioned.Add("Contoso.App_0.9.0.0_x64__8wekyb3d8bbwe");

        await Assert.ThrowsAsync<DeploymentStepException>(InstallAsync);
    }

    [Fact]
    public async Task TheApplicationsOwnFamilyRuleDecides()
    {
        TestMsix.WritePackage(At("Contoso.App.msix"), "Contoso.App");
        _machine.Provisioned.Add("Fabrikam.Tool_1.0.0.0_x64__8wekyb3d8bbwe");
        _machine.Provisioned.Remove(Kept);

        int exitCode = await InstallAsync(
            Tool with { Kind = ApplicationKind.Msix, Command = null, Detection = new PackageFamilyDetection("Fabrikam.Tool_8wekyb3d8bbwe") });

        Assert.Equal((0, 1), (exitCode, _tools.Calls.Count));
    }
}
