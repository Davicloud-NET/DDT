// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Contracts.Sequences;
using DDT.Core.CloudInit;
using Xunit;

namespace DDT.Agent.Tests;

// A dry run of a Linux sequence from the boot environment through the installed Linux to the end, put together the
// way the Windows program does. A file stands in for the disk, and a folder for the installed Linux.
public sealed class LinuxDryRunMachineTests : IDisposable
{
    private static readonly Guid s_machineId = Guid.Parse("0193a4b2-0000-7000-8000-000000000001");

    private static readonly WriteRawImageStep s_write = new() { Id = Guid.Parse("0193a4b2-0000-7000-8000-00000000e001"), Name = "Write the disk", ImageId = TestRawImage.ImageId };

    private static readonly WriteCloudInitSeedStep s_seed = new()
    {
        Id = Guid.Parse("0193a4b2-0000-7000-8000-00000000e002"),
        Name = "Seed",
        MetaData = "local-hostname: \"{{ComputerName}}\"\n",
        UserData = "#cloud-config\n",
    };

    // A dry run takes every script as exit code 0, so that is this script's restart code, and its success is another.
    private static readonly RunScriptStep s_restarts = new()
    {
        Id = Guid.Parse("0193a4b2-0000-7000-8000-00000000c001"),
        Name = "Install and restart",
        Phase = SequencePhase.Linux,
        Interpreter = ScriptInterpreter.Shell,
        Script = "apt-get -y install something\n",
        SuccessExitCodes = [1],
        RebootExitCodes = [0],
    };

    private static readonly SetVariableStep s_set = new() { Id = Guid.Parse("0193a4b2-0000-7000-8000-00000000c002"), Name = "Note it", Variable = "Installed", Value = "yes on {{ComputerName}}" };

    private static readonly RunScriptStep s_after = new()
    {
        Id = Guid.Parse("0193a4b2-0000-7000-8000-00000000c003"),
        Name = "After the restart",
        Phase = SequencePhase.Linux,
        Interpreter = ScriptInterpreter.Shell,
        Script = "echo \"$DDT_VAR_Installed\"\n",
        RebootExitCodes = [194],
    };

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ddt-dry-run-test-{Guid.NewGuid():N}");
    private readonly StringWriter _console = new();
    private readonly TestRawImage _image = new();

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        File.Delete(FileRawDisks.PathFor(_root, 0));
        File.Delete(AgentPath);
        _console.Dispose();
    }

    private string AgentPath => _root + "-agent";

    private AgentRun Run(DeploymentState state) =>
        new(
            TestRuns.RunId,
            state,
            "Install Linux",
            new SequenceDefinition(SequenceDefinition.CurrentVersion, [s_write, s_seed, s_restarts, s_set, s_after])
            {
                Variables = [new VariableDeclaration { Name = "Installed", SetBySteps = true }],
            },
            [_image.RunImage()],
            [],
            null,
            "LINUX-01");

    private static AgentRegistrationResult Continued() =>
        new(s_machineId, MachineState.Deploying, "session-2", "resume-2", 10, null, TestRuns.RunId, "run-token-2");

    // The boot environment registers and gets the run. Each start of the installed Linux registers with the run token.
    private ScriptedAgentServer Server()
    {
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer())
            .OnRegister(_ => new AgentRegistrationResult(s_machineId, MachineState.Approved, "session-0", "resume-0", 10, null))
            .OnNext(_ => new AgentNextResult(MachineState.Approved, "session-1", "resume", 10, null, Run: Run(DeploymentState.Assigned)))
            .OnRegister(_ => Continued())
            .OnNext(_ => new AgentNextResult(MachineState.Deploying, "session-3", "resume", 10, null, Run: Run(DeploymentState.Running)))
            .OnRegister(_ => Continued())
            .OnNext(_ => new AgentNextResult(MachineState.Deploying, "session-4", "resume", 10, null, Run: Run(DeploymentState.Running)));
        server.AnswerRunReports = (_, token) => new AgentRunReportResult(token, "resume", "run-token-1");

        return server;
    }

    private LinuxDryRunMachine Machine(ScriptedAgentServer server)
    {
        AgentLog log = new(new ImmediateTimeProvider(), _console);
        AgentOptions options = new(new Uri("https://ddt.example:8443/"), null, DryRun: true, DryRunId: 7, NoUpdate: false, KeyboardLayout: null)
        {
            DryRunEnvironment = AgentEnvironment.LinuxBoot,
        };

        return new(
            new DryRunMachineOptions(options, _root, AgentPath, Timeout.InfiniteTimeSpan, TestAgents.Version),
            server,
            staged => staged.ServerUrl.AbsoluteUri == "https://ddt.example:8443/" ? server : throw new InvalidOperationException("Another server was staged."),
            TestAgents.Status(new ScriptedSignInPrompt { IsAvailable = false }, log),
            log,
            new ImmediateTimeProvider())
        {
            FreeSpace = new DriveFreeSpace(),
            MakeSparse = SparseFile.Make,
        };
    }

    // What the console showed, without each line's time and level.
    private string[] Lines() =>
        [.. _console.ToString().Split(Environment.NewLine).Select(line => line.Length > 15 ? line[15..] : line)];

    [Fact]
    public async Task GoesOnInTheInstalledLinuxThroughARestartToTheEndOfTheRun()
    {
        await File.WriteAllBytesAsync(AgentPath, new byte[150_000], TestContext.Current.CancellationToken);
        ScriptedAgentServer server = Server();

        int exitCode = await Machine(server).RunAsync(server.Stop.Token);

        Assert.Equal(AgentExitCodes.Deployed, exitCode);
        Assert.Equal(
            [(AgentEnvironment.LinuxBoot, null), (AgentEnvironment.Linux, "run-token-1"), (AgentEnvironment.Linux, "run-token-1")],
            server.Registrations.Select(registration => (registration.Environment, registration.RunToken)));
        AgentRunReport done = server.RunReports[^1];
        Assert.Equal((DeploymentState.Done, SequencePhase.Linux), (done.State, done.Phase));
        Assert.Equal(Enumerable.Repeat(StepState.Done, 5), done.Steps.Select(step => step.State));
        Assert.Contains(server.RunReports, report => report.Variables?.GetValueOrDefault("Installed") == "yes on LINUX-01");
        Assert.Contains(server.RunReports, report => report is { Phase: SequencePhase.Linux, Activity: RunActivity.Restarting, State: DeploymentState.Running });

        // The root held the installed Linux and is gone with the run. The disk stays to be looked at, as it was handed
        // over but for the run's token, which the script wipes on a real seed. No byte of it is left in the file.
        Assert.False(Directory.Exists(_root));
        using FileStream disk = new(FileRawDisks.PathFor(_root, 0), FileMode.Open, FileAccess.Read, FileShare.Read);
        TestSeed seed = TestSeed.Read(disk);
        Assert.Equal(["ddt-agent", "agent.json", "state.json"], seed.Names(LinuxAgentFiles.SeedFolder));
        Assert.Equal(["meta-data", "user-data", "vendor-data", "ddt"], seed.Names());
        Assert.False(seed.Holds("run-token-1"));
    }

    // These lines, in the order the run went, each in the words of the system it was in.
    [Fact]
    public async Task SaysWhatTheInstalledLinuxWouldDo()
    {
        await File.WriteAllBytesAsync(AgentPath, new byte[150_000], TestContext.Current.CancellationToken);
        ScriptedAgentServer server = Server();
        string linux = Path.Combine(_root, "linux");
        string state = Path.Combine(linux, "var", "lib", "ddt");
        string scripts = Path.Combine(RunFiles.DirectoryIn(state), "scripts");

        Assert.Equal(AgentExitCodes.Deployed, await Machine(server).RunAsync(server.Stop.Token));

        string[] expected =
        [
            "Dry run: the fake machine started the Linux boot environment, which runs the sequences that write a raw disk image.",
            "The machine restarts into the installed Linux, where the agent goes on with the run.",
            "Dry run: no Linux is started. There cloud-init would run the agent's script from the seed, which installs the agent as " +
                $"ddt-agent.service. Here its settings and the run go from the seed in {FileRawDisks.PathFor(_root, 0)} to {state}.",
            "Dry run: the machine restarts into the installed Linux, which here starts its agent over there.",
            $"DDT agent {TestAgents.Version}, the ddt-agent.service unit of the installed Linux.",
            "Dry run: cloud-init counts as not running. In the installed Linux the agent would wait for /run/cloud-init/result.json, and then for a restart the user-data asks for.",
            $"Dry run: not run in {scripts}, and taken as exit code 0: {ToolRunner.CommandLine("/bin/sh", [Path.Combine(scripts, $"{s_restarts.Id:D}.sh")])}",
            "The script ended with exit code 0, which asks for a restart before the next step.",
            "Dry run: in the installed Linux the due restart would be recorded in /run/ddt/restart-due, which the restart clears.",
            "Linux restarts, and the run goes on after the restart.",
            "Dry run: not run: systemctl reboot",
            "Dry run: the machine restarts, which here starts its agent over.",
            $"Dry run: not run in {scripts}, and taken as exit code 0: {ToolRunner.CommandLine("/bin/sh", [Path.Combine(scripts, $"{s_after.Id:D}.sh")])}",
            "The run is done. Sending the last log lines.",
            "The run is over here, so the agent removes itself.",
            "Dry run: not run: systemctl disable ddt-agent.service",
            "Dry run: not run: systemctl daemon-reload",
        ];
        string[] lines = Lines();
        Assert.Equal(expected, lines.Where(expected.Contains).Distinct());
        Assert.Equal(2, lines.Count(line => line.StartsWith("Dry run: the installed Linux starts the agent's unit, which here goes on in this process with ", StringComparison.Ordinal)));
        Assert.DoesNotContain(lines, line => line.Contains("run-token", StringComparison.Ordinal) || line.Contains("Windows", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.StartsWith("WARN", StringComparison.Ordinal) || line.Contains("could not", StringComparison.Ordinal));
    }

    // A run that ends in the boot environment restarts into the disk, and nothing of an installed Linux is played.
    [Fact]
    public async Task ARunWithoutStepsInTheInstalledLinuxEndsInTheBootEnvironmentAsBefore()
    {
        AgentRun run = Run(DeploymentState.Assigned) with { Sequence = new SequenceDefinition(SequenceDefinition.CurrentVersion, [s_write, s_seed]) };
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer())
            .OnRegister(_ => new AgentRegistrationResult(s_machineId, MachineState.Approved, "session-0", "resume-0", 10, null))
            .OnNext(_ => new AgentNextResult(MachineState.Approved, "session-1", "resume", 10, null, Run: run));

        Assert.Equal(AgentExitCodes.Deployed, await Machine(server).RunAsync(server.Stop.Token));

        Assert.Equal(AgentEnvironment.LinuxBoot, Assert.Single(server.Registrations).Environment);
        Assert.DoesNotContain(Lines(), line => line.Contains("installed Linux", StringComparison.Ordinal));
    }
}
