// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Applications;
using DDT.Contracts.Agents;
using DDT.Contracts.Applications;
using DDT.Core.Sequences;
using Xunit;
using static DDT.Agent.Tests.TestApplications;

namespace DDT.Agent.Tests;

// The step across restarts: those its applications ask for, and those that cut an installer off.
public sealed class InstallApplicationsResumeTests
{
    private static readonly FileDetection s_toolFile = new(@"%ProgramFiles%\Tool\tool.exe", "2.0");

    [Fact]
    public async Task ARestartCodeWithApplicationsLeftRestartsAndGoesOnLater()
    {
        using ApplicationRunFixture run = new ApplicationRunFixture().Serve(Runtime, Tool);
        run.Installer.ExitCode = _ => 3010;

        StepResult result = await run.RunAsync();

        Assert.Equal(StepOutcome.RestartAndContinue, result.Outcome);
        Assert.Equal(["save", "install Runtime"], run.Events);
        ApplicationProgress kept = ApplicationProgress.Read(result.Progress, [Runtime, Tool]);
        Assert.Equal((ApplicationState.Installed, ApplicationState.Pending), (kept.StateOf(Runtime.Id), kept.StateOf(Tool.Id)));
        Assert.Equal(
            ["Installing Runtime.", "Installed Runtime, which asks for a restart with exit code 3010.", InstallApplicationsStepRunner.GoesOn],
            (await run.SentLinesAsync()).Select(line => line.Message));
    }

    [Fact]
    public async Task AfterTheRestartItGoesOnWithTheNextApplication()
    {
        using ApplicationRunFixture run = new ApplicationRunFixture().Serve(Runtime, Tool);

        StepResult result = await run.RunAsync(run.Turn(Saved((Runtime, ApplicationState.Installed, 0))));

        Assert.Equal(StepResult.Done(), result);
        Assert.Equal(["save", "install Tool"], run.Events);
        Assert.Equal("Applications: 2 installed.", (await run.SentLinesAsync())[^1].Message);
    }

    [Fact]
    public async Task ARestartCodeOnTheLastApplicationEndsTheStepWithARestart()
    {
        using ApplicationRunFixture run = new ApplicationRunFixture().Serve(Runtime, Tool);
        run.Installer.ExitCode = launch => launch.Application.Name == "Tool" ? 3010 : 0;

        StepResult result = await run.RunAsync();

        Assert.Equal(StepResult.RebootRequired(), result);
        Assert.Equal(["save", "install Runtime", "save", "install Tool"], run.Events);
    }

    // The list the step runs is the server's, and the progress is matched to it by id.
    [Fact]
    public async Task AnApplicationInTheProgressThatIsNoLongerInTheListIsLeftOut()
    {
        using ApplicationRunFixture run = new ApplicationRunFixture().Serve(Runtime, Tool);
        string saved = Saved((Viewer, ApplicationState.Failed, 0), (Runtime, ApplicationState.Installed, 0));

        StepResult result = await run.RunAsync(run.Turn(saved));

        Assert.Equal(StepResult.Done(), result);
        Assert.Equal(["save", "install Tool"], run.Events);
        Assert.DoesNotContain(Viewer.Id.ToString("D"), run.Saves[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task AProgressThatCannotBeReadStartsOver()
    {
        using ApplicationRunFixture run = new ApplicationRunFixture().Serve(Tool);

        await run.RunAsync(run.Turn("not json"));

        Assert.Equal(["save", "install Tool"], run.Events);
    }

    [Fact]
    public async Task AnInterruptedInstallerItsRuleFindsCountsAsInstalled()
    {
        AgentApplication tool = Tool with { Detection = s_toolFile };
        using ApplicationRunFixture run = new ApplicationRunFixture().Serve(tool);
        run.Detector.Installed.Add(s_toolFile);

        StepResult result = await run.RunAsync(run.Turn(Saved((tool, ApplicationState.Started, 1)), interrupted: true));

        Assert.Equal(StepResult.Done(), result);
        Assert.Empty(run.Events);
        Assert.Equal(
            [@"Tool was installed before the machine restarted, as %ProgramFiles%\Tool\tool.exe at version 2.0 or later shows.", "Applications: 1 installed."],
            (await run.SentLinesAsync()).Select(line => line.Message));
    }

    [Fact]
    public async Task AnInterruptedInstallerItsRuleDoesNotFindRunsOnceMore()
    {
        AgentApplication tool = Tool with { Detection = s_toolFile };
        using ApplicationRunFixture run = new ApplicationRunFixture().Serve(tool);

        StepResult result = await run.RunAsync(run.Turn(Saved((tool, ApplicationState.Started, 1)), interrupted: true));

        Assert.Equal(StepResult.Done(), result);
        Assert.Equal(["save", "install Tool"], run.Events);
        Assert.Equal(2, ApplicationProgress.Read(run.Saves[0], [tool]).AttemptsOf(tool.Id));
    }

    [Fact]
    public async Task AnInterruptedInstallerWithoutARuleRunsOnceMore()
    {
        using ApplicationRunFixture run = new ApplicationRunFixture().Serve(Runtime, Tool);

        StepResult result = await run.RunAsync(run.Turn(Saved((Runtime, ApplicationState.Installed, 0), (Tool, ApplicationState.Started, 1)), interrupted: true));

        Assert.Equal(StepResult.Done(), result);
        Assert.Equal(["save", "install Tool"], run.Events);
        Assert.Equal(2, ApplicationProgress.Read(run.Saves[0], [Runtime, Tool]).AttemptsOf(Tool.Id));
        Assert.Contains(
            "Installing Tool again, because the machine restarted while it installed.",
            (await run.SentLinesAsync()).Select(line => line.Message));
    }

    [Fact]
    public async Task AnInstallerInterruptedTwiceFailsTheStepNamingIt()
    {
        using ApplicationRunFixture run = new ApplicationRunFixture().Serve(Tool);

        StepResult result = await run.RunAsync(run.Turn(Saved((Tool, ApplicationState.Started, 2)), interrupted: true));

        Assert.Equal(StepResult.Failed("The machine restarted twice while Tool installed, so it is not installed a third time."), result);
        Assert.Empty(run.Events);
    }

    // What a turn kept, as the runner writes it.
    private static string Saved(params (AgentApplication Application, ApplicationState State, int Attempts)[] marks)
    {
        ApplicationProgress progress = ApplicationProgress.Read(null, [.. marks.Select(mark => mark.Application)]);

        foreach ((AgentApplication application, ApplicationState state, int attempts) in marks)
        {
            if (state == ApplicationState.Started)
            {
                progress.Start(application.Id, attempts);
            }
            else
            {
                progress.Decide(application.Id, state);
            }
        }

        return progress.Write();
    }
}
