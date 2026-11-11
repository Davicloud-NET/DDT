// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Applications;
using DDT.Agent.Deployment;
using DDT.Contracts.Agents;
using DDT.Contracts.Applications;
using DDT.Contracts.Deployments;
using DDT.Core.Sequences;
using Xunit;
using static DDT.Agent.Tests.TestApplications;

namespace DDT.Agent.Tests;

public sealed class InstallApplicationsStepRunnerTests
{
    [Fact]
    public async Task InstallsTheApplicationsInTheServersOrderAndSaysHowItWent()
    {
        using ApplicationRunFixture run = new ApplicationRunFixture().Serve(Runtime, Tool);

        StepResult result = await run.RunAsync();

        Assert.Equal(StepResult.Done(), result);
        Assert.Equal(["save", "install Runtime", "save", "install Tool"], run.Events);
        Assert.Equal([50, 100], run.Progress.Values.Where(value => value % 50 == 0 && value > 0).Distinct());
        Assert.Equal(
            [
                (AgentLogLevel.Information, "Installing Runtime."),
                (AgentLogLevel.Information, "Installed Runtime."),
                (AgentLogLevel.Information, "Installing Tool."),
                (AgentLogLevel.Information, "Installed Tool."),
                (AgentLogLevel.Information, "Applications: 2 installed."),
            ],
            (await run.SentLinesAsync()).Select(line => (line.Level, line.Message)));
    }

    // The server only hands the list out for the step it knows is running.
    [Fact]
    public async Task ReportsTheStepRunningBeforeItAsksForTheApplications()
    {
        using ApplicationRunFixture run = new ApplicationRunFixture().Serve(Tool);

        await run.RunAsync();

        Assert.Equal(
            [$"run-report {DeploymentState.Running} session", $"run-applications {ApplicationRunFixture.Step.Id} session"],
            run.Server.Calls.Take(2));
    }

    [Fact]
    public async Task AStepWithoutApplicationsIsDone()
    {
        using ApplicationRunFixture run = new ApplicationRunFixture().Serve();

        StepResult result = await run.RunAsync();

        Assert.Equal(StepResult.Done(), result);
        Assert.Empty(run.Events);
        Assert.Equal(InstallApplicationsStepRunner.Empty, Assert.Single(await run.SentLinesAsync()).Message);
    }

    // So a restart during the installer finds it started.
    [Fact]
    public async Task SavesTheApplicationAsStartedBeforeItsInstallerRuns()
    {
        using ApplicationRunFixture run = new ApplicationRunFixture().Serve(Tool);

        await run.RunAsync();

        Assert.Equal(["save", "install Tool"], run.Events);
        ApplicationProgress saved = ApplicationProgress.Read(Assert.Single(run.Saves), [Tool]);
        Assert.Equal((ApplicationState.Started, 1), (saved.StateOf(Tool.Id), saved.AttemptsOf(Tool.Id)));
    }

    [Fact]
    public async Task AnInstallerWithoutFilesRunsInTheScriptsFolderWithTheRunsValues()
    {
        using ApplicationRunFixture run = new ApplicationRunFixture().Serve(Tool);

        await run.RunAsync(run.Turn(values: new Dictionary<string, string> { ["Office"] = "Vienna" }));

        ApplicationLaunch launch = Assert.Single(run.Installer.Launches);
        string scripts = Path.Combine(run.RunDirectory, "scripts");
        Assert.Equal((scripts, scripts), (launch.WorkingDirectory, launch.Scripts));
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["DDT_RUN_ID"] = StepRunnerFixture.RunId.ToString("D"),
                ["DDT_STEP_ID"] = ApplicationRunFixture.Step.Id.ToString("D"),
                ["DDT_APPLICATION"] = "Tool",
                ["DDT_VAR_Office"] = "Vienna",
            },
            launch.Environment);
    }

    [Fact]
    public async Task UnpacksAnApplicationsFilesForItsInstallerAndDeletesThemAfterwards()
    {
        TestZip zip = new(("setup.cmd", "echo setup"));
        AgentApplication tool = Tool with { Files = new AgentApplicationFiles(zip.Sha256, zip.Content.Length, 10, "tool.zip") };
        using ApplicationRunFixture run = new ApplicationRunFixture().Serve(tool);
        run.Server.ServeFile(zip.Sha256, zip.Content);
        string package = Path.Combine(run.RunDirectory, "packages", ApplicationRunFixture.Step.Id.ToString("D"), tool.Id.ToString("D"));
        bool unpacked = false;
        run.Installer.Seen = launch => unpacked = File.Exists(Path.Combine(launch.WorkingDirectory, "setup.cmd"));

        StepResult result = await run.RunAsync();

        Assert.Equal(StepResult.Done(), result);
        ApplicationLaunch launch = Assert.Single(run.Installer.Launches);
        Assert.Equal((package, package), (launch.WorkingDirectory, launch.Environment["DDT_PACKAGE"]));
        Assert.True(unpacked);
        Assert.False(Directory.Exists(package));
    }

    [Fact]
    public async Task TooLittleFreeSpaceFailsTheApplicationBeforeItsFilesDownload()
    {
        AgentApplication tool = Tool with { Files = new AgentApplicationFiles(new string('a', 64), 1024L * 1024 * 1024, 1024L * 1024 * 1024, "tool.zip") };
        using ApplicationRunFixture run = new ApplicationRunFixture().Serve(tool);
        run.FreeSpace.Available = 2L * 1024 * 1024 * 1024;

        StepResult result = await run.RunAsync();

        Assert.Equal(
            StepResult.Failed("Tool could not be installed: Its files need 3 GB free to download and unpack, 1 GB to spare included, and the disk has 2 GB free."),
            result);
        Assert.Empty(run.Events);
        Assert.DoesNotContain(run.Server.Calls, call => call.StartsWith("open-file", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnApplicationItsRuleFindsIsNotInstalledAgain()
    {
        AgentApplication tool = Tool with { Detection = new ProductCodeDetection("{23170F69-40C1-2702-2301-000001000000}") };
        using ApplicationRunFixture run = new ApplicationRunFixture().Serve(tool);
        run.Detector.Installed.Add(new ProductCodeDetection("{23170F69-40C1-2702-2301-000001000000}"));

        StepResult result = await run.RunAsync();

        Assert.Equal(StepResult.Done(), result);
        Assert.Empty(run.Events);
        Assert.Equal(
            [
                "Tool is installed already, as the product code {23170F69-40C1-2702-2301-000001000000} shows, so it was not installed again.",
                "Applications: 0 installed, 1 there already.",
            ],
            (await run.SentLinesAsync()).Select(line => line.Message));
    }

    [Fact]
    public async Task AnApplicationItsRuleDoesNotFindIsInstalled()
    {
        AgentApplication tool = Tool with { Detection = new UninstallKeyDetection("Tool") };
        using ApplicationRunFixture run = new ApplicationRunFixture().Serve(tool);

        await run.RunAsync();

        Assert.Equal(["save", "install Tool"], run.Events);
        Assert.Equal(new UninstallKeyDetection("Tool"), Assert.Single(run.Detector.Asked));
    }

    [Fact]
    public async Task AFailedApplicationFailsTheStepWithItsExitCodeAndNothingAfterItIsInstalled()
    {
        using ApplicationRunFixture run = new ApplicationRunFixture().Serve(Runtime, Tool);
        run.Installer.ExitCode = _ => 1603;

        StepResult result = await run.RunAsync();

        Assert.Equal(StepResult.Failed("Runtime ended with exit code 1603, which is not one of its success codes (0).") with { ExitCode = 1603 }, result);
        Assert.Equal(["save", "install Runtime"], run.Events);
    }

    [Fact]
    public async Task AFailedApplicationThatMayFailLetsTheStepGoOnAndSkipsWhatDependsOnIt()
    {
        AgentApplication runtime = Runtime with { ContinueOnFailure = true };
        AgentApplication tool = Tool with { DependsOn = [Runtime.Id] };
        using ApplicationRunFixture run = new ApplicationRunFixture().Serve(runtime, tool, Viewer);
        run.Installer.ExitCode = launch => launch.Application.Name == "Runtime" ? 1 : 0;

        StepResult result = await run.RunAsync();

        Assert.Equal(StepResult.Done(), result);
        Assert.Equal(["save", "install Runtime", "save", "install Viewer"], run.Events);
        List<AgentLogLine> lines = await run.SentLinesAsync();
        Assert.Equal(
            [
                (AgentLogLevel.Warning, "Runtime ended with exit code 1, which is not one of its success codes (0). The step goes on, because Runtime may fail."),
                (AgentLogLevel.Information, "Skipped Tool, because it depends on Runtime, which failed."),
                (AgentLogLevel.Warning, "Applications: 1 installed, 1 skipped, 1 failed. The step is done, because each that failed may fail."),
            ],
            lines.Where(line => !line.Message.StartsWith("Install", StringComparison.Ordinal)).Select(line => (line.Level, line.Message)));
    }

    [Fact]
    public async Task AnInstallerThatRunsOutOfTimeFailsItsApplication()
    {
        using ApplicationRunFixture run = new ApplicationRunFixture().Serve(Tool);
        run.Installer.ExitCode = _ => throw new DeploymentStepException("cmd.exe was still running after 5 minutes, so it was stopped with every process it started.");

        StepResult result = await run.RunAsync();

        Assert.Equal(
            StepResult.Failed("Tool could not be installed: cmd.exe was still running after 5 minutes, so it was stopped with every process it started."),
            result);
    }

    [Theory]
    [InlineData(ApplicationKind.Msix, "Tool is of a kind this agent cannot install.")]
    [InlineData(ApplicationKind.Winget, "Tool is a winget package, which this agent cannot install yet.")]
    public async Task AKindThisAgentCannotInstallFailsItsApplication(ApplicationKind kind, string error)
    {
        using ApplicationRunFixture run = new ApplicationRunFixture().Serve(Tool with { Kind = kind, Command = null });

        StepResult result = await run.RunAsync();

        Assert.Equal(StepResult.Failed(error), result);
        Assert.Empty(run.Events);
    }
}
