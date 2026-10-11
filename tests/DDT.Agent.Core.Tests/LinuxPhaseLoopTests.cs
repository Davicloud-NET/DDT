// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Agent.LinuxPhase;
using DDT.Agent.Sequences;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

// The agent in the installed Linux, from the state the hand-over leaves to its own removal.
public sealed class LinuxPhaseLoopTests : IDisposable
{
    private readonly LinuxPhaseFixture _linux = new();

    public void Dispose() => _linux.Dispose();

    private string ScriptFile(RunScriptStep step) => Path.Combine(_linux.RunDirectory, "scripts", $"{step.Id:D}.sh");

    private static ScriptedAgentServer Server(AgentRun run) =>
        new ScriptedAgentServer()
            .OnRegister(_ => LinuxPhaseFixture.Continued())
            .OnNext(_ => LinuxPhaseFixture.Next("session-1", run));

    [Fact]
    public async Task GoesOnWithTheRunInTheInstalledLinuxAndRemovesItselfWhenItIsDone()
    {
        RunScriptStep first = LinuxPhaseFixture.Script(1);
        RunScriptStep second = LinuxPhaseFixture.Script(2) with { Script = "#!/bin/bash\necho 2\n" };
        AgentRun run = LinuxPhaseFixture.Run(first, second);
        await _linux.HandOverAsync(run);
        ScriptedAgentServer server = Server(run);

        int exitCode = await _linux.RunAsync(server);

        Assert.Equal(AgentExitCodes.Deployed, exitCode);
        AgentRegistration registration = Assert.Single(server.Registrations);
        Assert.Equal(
            ("run-token-1", AgentEnvironment.Linux, SupportedSequences.Version, null),
            (registration.RunToken, registration.Environment, registration.SequenceVersion, registration.ResumeToken));

        // The scripts are on the disk under the state folder, because /run starts no program. Then the removal.
        Assert.Equal(
            [
                RecordingToolRunner.CommandLine(ShellScriptLauncher.DefaultShell, ScriptFile(first)),
                RecordingToolRunner.CommandLine("/bin/bash", ScriptFile(second)),
                "systemctl disable ddt-agent.service",
                "systemctl daemon-reload",
            ],
            _linux.Tools.Calls);
        ToolRunOptions options = _linux.Tools.Options[0];
        Assert.Equal(Path.Combine(_linux.RunDirectory, "scripts"), options.WorkingDirectory);
        Assert.Equal(("Linux", "/usr/sbin:/usr/bin", "/root"), (options.Environment?["DDT_PHASE"], options.Environment?["PATH"], options.Environment?["HOME"]));
        Assert.False(options.Environment?.ContainsKey(RunScriptStepRunner.Disk));

        Assert.All(server.RunReports, report => Assert.Equal(SequencePhase.Linux, report.Phase));
        Assert.Equal(DeploymentState.Done, server.RunReports[^1].State);
        Assert.Equal(Enumerable.Repeat(StepState.Done, 4), server.RunReports[^1].Steps.Select(step => step.State));
        Assert.True(_linux.IsRemoved());
        Assert.DoesNotContain(_linux.Lines(), line => line.Contains("Windows", StringComparison.Ordinal));
        Assert.DoesNotContain("run-token", _linux.Console.ToString(), StringComparison.Ordinal);
    }

    // What the machine's log shows of a run with one step, from the unit's start to the removal.
    [Fact]
    public async Task SaysInOrderWhatItDoesFromTheUnitsStartToItsRemoval()
    {
        AgentRun run = LinuxPhaseFixture.Run(LinuxPhaseFixture.Script(1));
        await _linux.HandOverAsync(run);

        await _linux.RunAsync(Server(run));

        string[] expected =
        [
            $"DDT agent {TestAgents.Version}, the ddt-agent.service unit of the installed Linux.",
            "cloud-init does not run at this start, so the run does not wait for it.",
            "The run of Install Linux goes on at step Script 1.",
            "Step Script 1 begins.",
            "The run is done. Sending the last log lines.",
            "The run is over here, so the agent removes itself.",
            "The agent is removed from this Linux, but for what a line above says stays.",
        ];
        Assert.Equal(expected, _linux.Lines().Where(expected.Contains));
    }

    [Fact]
    public async Task ARestartCodeRestartsLinuxThroughSystemdAndTheRunGoesOnAfterIt()
    {
        RunScriptStep restarts = LinuxPhaseFixture.Script(1);
        RunScriptStep after = LinuxPhaseFixture.Script(2);
        AgentRun run = LinuxPhaseFixture.Run(restarts, after);
        await _linux.HandOverAsync(run);
        bool recordedFirst = false;
        _linux.Tools.AnswerExitCode = (_, arguments, _) => arguments[^1] == ScriptFile(restarts) ? 194 : 0;
        ScriptedAgentServer first = Server(run);
        first.AnswerRunReports = (report, token) =>
        {
            recordedFirst |= report.Activity == RunActivity.Restarting && File.Exists(Path.Combine(_linux.Paths.Run, LinuxRestartMarker.FileName));

            return new AgentRunReportResult(token, "resume", "run-token-3");
        };

        Assert.Equal(AgentExitCodes.Restarting, await _linux.RunAsync(first));

        // The restart is recorded before the server hears of it, and systemd is asked once the last lines are sent.
        Assert.True(recordedFirst);
        Assert.Equal([RecordingToolRunner.CommandLine(ShellScriptLauncher.DefaultShell, ScriptFile(restarts)), "systemctl reboot"], _linux.Tools.Calls);
        Assert.Contains("The script ended with exit code 194, which asks for a restart before the next step.", _linux.Lines());
        Assert.Contains("Linux restarts, and the run goes on after the restart.", _linux.Lines());
        Assert.Equal("run-token-3", await _linux.Files.LoadTokenAsync(TestContext.Current.CancellationToken));

        // Linux starts again: /run is empty, and the unit starts the agent with the state on the disk.
        Directory.Delete(_linux.Paths.Run, recursive: true);
        ScriptedAgentServer second = Server(run);

        Assert.Equal(AgentExitCodes.Deployed, await _linux.RunAsync(second));

        Assert.Equal("run-token-3", Assert.Single(second.Registrations).RunToken);
        Assert.Equal(RecordingToolRunner.CommandLine(ShellScriptLauncher.DefaultShell, ScriptFile(after)), _linux.Tools.Calls[2]);
        Assert.Equal(Enumerable.Repeat(StepState.Done, 4), second.RunReports[^1].Steps.Select(step => step.State));
        Assert.True(_linux.IsRemoved());
    }

    // The unit was stopped between the step that asked for the restart and the restart itself, and started again in
    // the same Linux. The state says the step is done, so Linux restarts before anything else.
    [Fact]
    public async Task AStopBeforeTheRestartRestartsLinuxAtTheNextStart()
    {
        AgentRun run = LinuxPhaseFixture.Run(new RebootStep { Id = Guid.Parse("0193a4b2-0000-7000-8000-00000000b004"), Name = "Restart" }, LinuxPhaseFixture.Script(2));
        await _linux.HandOverAsync(run);
        ScriptedAgentServer first = Server(run);
        first.AnswerRunReports = (report, token) =>
        {
            if (report.Activity == RunActivity.Restarting)
            {
                first.Stop.Cancel();
            }

            return new AgentRunReportResult(token, "resume", null);
        };

        Assert.Equal(AgentExitCodes.Stopped, await _linux.RunAsync(first));
        Assert.Empty(_linux.Tools.Calls);

        ScriptedAgentServer second = new();

        Assert.Equal(AgentExitCodes.Restarting, await _linux.RunAsync(second));

        Assert.Empty(second.Registrations);
        Assert.Equal(["systemctl reboot"], _linux.Tools.Calls);
        Assert.Contains("Linux was to restart for the run but has not restarted since. Restarting it now.", _linux.Lines());
    }

    [Fact]
    public async Task FallsBackToRebootWhenSystemdDoesNotRestartLinux()
    {
        AgentRun run = LinuxPhaseFixture.Run(new RebootStep { Id = Guid.Parse("0193a4b2-0000-7000-8000-00000000b004"), Name = "Restart" });
        await _linux.HandOverAsync(run);
        ScriptedAgentServer server = Server(run);
        _linux.Tools.Answer = (fileName, _) =>
        {
            if (fileName == LinuxPhaseRebooter.Systemctl)
            {
                throw new DeploymentStepException("systemctl failed with exit code 0x00000001. Its output is in the machine log.");
            }

            server.Stop.Cancel();

            return [];
        };

        Assert.Equal(AgentExitCodes.Restarting, await _linux.RunAsync(server));

        Assert.Equal(["systemctl reboot", "reboot"], _linux.Tools.Calls);
    }

    // Nobody sits at a machine in the installed Linux, so the web continues a pause there, as in Windows.
    [Fact]
    public async Task APauseInTheInstalledLinuxIsContinuedFromTheWebAndAVariableIsSetAfterIt()
    {
        PauseStep pause = new() { Id = Guid.Parse("0193a4b2-0000-7000-8000-00000000b007"), Name = "Wait for the cable", Message = "Plug {{ComputerName}} in." };
        SetVariableStep set = new() { Id = Guid.Parse("0193a4b2-0000-7000-8000-00000000b008"), Name = "Note it", Variable = "Plugged", Value = "yes" };
        AgentRun run = LinuxPhaseFixture.Run(pause, set, LinuxPhaseFixture.Script(1));
        run = run with { Sequence = run.Sequence with { Variables = [new VariableDeclaration { Name = "Plugged", SetBySteps = true }] } };
        await _linux.HandOverAsync(run);
        ScriptedAgentServer server = Server(run);
        List<string?> messages = [];
        server.AnswerRunReports = (report, token) =>
        {
            messages.Add(report.PauseMessage);

            return report.Activity == RunActivity.Paused
                ? new AgentRunReportResult(token, "resume", null, ContinueStepId: pause.Id, ContinuePass: 1)
                : new AgentRunReportResult(token, "resume", null);
        };

        Assert.Equal(AgentExitCodes.Deployed, await _linux.RunAsync(server));

        Assert.Contains("Plug LINUX-01 in.", messages);
        Assert.Contains("Someone continued the run on the web.", _linux.Lines());
        Assert.Equal("yes", _linux.Tools.Options[0].Environment?["DDT_VAR_Plugged"]);
        Assert.Equal(Enumerable.Repeat(StepState.Done, 5), server.RunReports[^1].Steps.Select(step => step.State));
    }

    // The run waits before its first step is marked as running, and the server sees what it waits for.
    [Fact]
    public async Task WaitsForCloudInitBeforeTheFirstStepAndTellsTheServer()
    {
        RunScriptStep script = LinuxPhaseFixture.Script(1);
        AgentRun run = LinuxPhaseFixture.Run(script);
        await _linux.HandOverAsync(run);
        _linux.Put(_linux.Paths.At(CloudInitFiles.EnabledPath));
        ManualTimeProvider time = new();
        ScriptedAgentServer server = Server(run);

        Task<int> running = _linux.RunAsync(server, time);
        await time.AdvanceUntilAsync(LinuxPhaseProfile.PollInterval, () => server.RunReports.Count > 0);

        AgentRunReport waiting = server.RunReports[^1];
        Assert.Equal((DeploymentState.Running, RunActivity.WaitingForCloudInit, SequencePhase.Linux), (waiting.State, waiting.Activity, waiting.Phase));
        Assert.DoesNotContain(waiting.Steps, step => step.StepId == script.Id && step.State != StepState.Pending);
        Assert.Empty(_linux.Tools.Calls);

        _linux.Put(_linux.Paths.At(CloudInitFiles.ResultPath), """{ "v1": { "datasource": "DataSourceNoCloud", "errors": [] } }""");
        await time.AdvanceUntilAsync(LinuxPhaseProfile.PollInterval, () => running.IsCompleted);

        Assert.Equal(AgentExitCodes.Deployed, await running);
        string[] expected =
        [
            "Waiting for cloud-init, which sets this Linux up at its first start: cloud-init is still running. The run goes on after it.",
            "cloud-init has finished at this start.",
            "The wait for cloud-init is over, and the run goes on.",
            "Step Script 1 begins.",
        ];
        Assert.Equal(expected, _linux.Lines().Where(expected.Contains));
    }

    // A zip carries no modes, so every file of a package can be started. The package goes with its step.
    [Fact]
    public async Task UnpacksAScriptsPackageUnderTheStateFolderWithFilesThatCanBeStarted()
    {
        TestZip zip = new(("install.sh", "#!/bin/sh\n"), ("bin/tool", "ELF"));
        RunScriptStep script = LinuxPhaseFixture.Script(1) with { PackageId = Guid.NewGuid() };
        AgentRun run = LinuxPhaseFixture.Run([script], [new AgentRunPackage(script.Id, "Tools", zip.Sha256, zip.Content.Length)]);
        await _linux.HandOverAsync(run);
        ScriptedAgentServer server = Server(run).ServeFile(zip.Sha256, zip.Content);
        string package = Path.Combine(_linux.RunDirectory, "packages", $"{script.Id:D}");
        List<string> unpacked = [];
        _linux.Tools.AnswerExitCode = (_, _, options) =>
        {
            foreach (string file in Directory.GetFiles(package, "*", SearchOption.AllDirectories))
            {
                bool canStart = OperatingSystem.IsWindows() || File.GetUnixFileMode(file).HasFlag(UnixFileMode.UserExecute);
                unpacked.Add($"{Path.GetRelativePath(package, file).Replace('\\', '/')} {(canStart ? "can start" : "cannot start")}");
            }

            return options.WorkingDirectory == package && options.Environment?["DDT_PACKAGE"] == package ? 0 : 1;
        };

        Assert.Equal(AgentExitCodes.Deployed, await _linux.RunAsync(server));

        Assert.Equal(["bin/tool can start", "install.sh can start"], unpacked.Order());
        Assert.Equal(DeploymentState.Done, server.RunReports[^1].State);
    }

    [Fact]
    public async Task GivesAScriptItsSharesAsPlacesUnderRunAndTakesThemAwayAfterIt()
    {
        RunScriptStep script = LinuxPhaseFixture.Script(1) with { Shares = [new ShareConnection(@"\\files.example.test\drivers", new AccountReference(Guid.NewGuid(), null))] };
        AgentRun run = LinuxPhaseFixture.Run(script);
        await _linux.HandOverAsync(run);
        ScriptedAgentServer server = Server(run)
            .OnRunStepAccounts(_ => new AgentStepAccounts(null, [new AgentShareConnection(@"\\files.example.test\drivers", @"CORP\ddt", "made,up")]));
        string place = Path.Combine(_linux.Paths.Shares, $"{script.Id:D}", "1");
        List<string> mountedDuringTheStep = [];
        _linux.Tools.AnswerExitCode = (_, _, options) =>
        {
            mountedDuringTheStep.AddRange(_linux.Mounts.Calls);

            return options.Environment?["DDT_SHARE_1"] == place && Directory.Exists(place) ? 0 : 1;
        };

        Assert.Equal(AgentExitCodes.Deployed, await _linux.RunAsync(server));

        Assert.Equal([$"mount cifs //files.example.test/drivers at {place}"], mountedDuringTheStep);
        Assert.Equal($"unmount {place} detached", _linux.Mounts.Calls[^1]);
        Assert.Equal("ip=10.0.2.2,username=ddt,domain=CORP,password=made,,up,sec=ntlmssp,file_mode=0700,dir_mode=0700", Assert.Single(_linux.Mounts.Options));
        Assert.Equal(DeploymentState.Done, server.RunReports[^1].State);
        Assert.DoesNotContain("made,", _linux.Console.ToString(), StringComparison.Ordinal);
    }

    // The validator lets no such step through, and an agent asked anyway asks the server for no account.
    [Fact]
    public async Task RefusesInOneSentenceToRunAScriptAsAnAccount()
    {
        RunScriptStep script = LinuxPhaseFixture.Script(1) with { RunAs = new AccountReference(Guid.NewGuid(), null) };
        AgentRun run = LinuxPhaseFixture.Run(script);
        await _linux.HandOverAsync(run);
        ScriptedAgentServer server = Server(run);

        Assert.Equal(AgentExitCodes.Stopped, await _linux.RunAsync(server));

        AgentRunReport failed = server.RunReports[^1];
        Assert.Equal(DeploymentState.Failed, failed.State);
        Assert.Equal(StepAccounts.RunAsInLinux, failed.Steps[^1].Error);
        Assert.DoesNotContain(_linux.Tools.Calls, call => call.Contains(".sh", StringComparison.Ordinal));

        // A failed run leaves no agent behind either.
        Assert.True(_linux.IsRemoved());
    }

    // A step of the boot environment after the hand-over is refused by the validator, and by the agent in its words.
    [Fact]
    public async Task FailsARunWhoseNextStepBelongsToTheBootEnvironment()
    {
        RunScriptStep boot = LinuxPhaseFixture.Script(2) with { Phase = SequencePhase.LinuxBoot, RebootExitCodes = [] };
        AgentRun run = LinuxPhaseFixture.Run(LinuxPhaseFixture.Script(1), boot);
        await _linux.HandOverAsync(run);
        ScriptedAgentServer server = Server(run);

        Assert.Equal(AgentExitCodes.Stopped, await _linux.RunAsync(server));

        Assert.Equal(
            (DeploymentState.Failed, EnvironmentTexts.BootStepAfterInstalled(AgentEnvironment.Linux)),
            (server.RunReports[^1].State, server.RunReports[^1].Error));
        Assert.True(_linux.IsRemoved());
    }
}
