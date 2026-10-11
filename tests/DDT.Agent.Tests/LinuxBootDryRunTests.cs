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
using DDT.Core.Disks;
using Xunit;

namespace DDT.Agent.Tests;

// A dry run like the agent runs it with --dry-run --dry-run-environment LinuxBoot, put together the way the Windows
// program does: the fake machine starts the Linux boot environment, and a file stands in for its disk.
public sealed class LinuxBootDryRunTests : IDisposable
{
    private static readonly Guid s_machineId = Guid.Parse("0193a4b2-0000-7000-8000-000000000001");

    private static readonly RunScriptStep s_script = new()
    {
        Id = Guid.Parse("0193a4b2-0000-7000-8000-00000000c001"),
        Name = "Before the disk",
        Phase = SequencePhase.LinuxBoot,
        Interpreter = ScriptInterpreter.Shell,
        Script = "#!/bin/bash\necho \"$DDT_PHASE\"\n",
        RebootExitCodes = [],
    };

    private static readonly WriteRawImageStep s_write = new()
    {
        Id = Guid.Parse("0193a4b2-0000-7000-8000-00000000e001"),
        Name = "Write the disk",
        ImageId = TestRawImage.ImageId,
    };

    private static readonly WriteCloudInitSeedStep s_seed = new()
    {
        Id = Guid.Parse("0193a4b2-0000-7000-8000-00000000e002"),
        Name = "Seed",
        MetaData = "local-hostname: \"{{ComputerName}}\"\n",
        UserData = "#cloud-config\n",
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
        _console.Dispose();
    }

    private AgentRun Run(params SequenceStep[] steps) =>
        new(
            TestRuns.RunId,
            DeploymentState.Assigned,
            "Install Linux",
            new SequenceDefinition(SequenceDefinition.CurrentVersion, steps),
            steps.OfType<WriteRawImageStep>().Any() ? [_image.RunImage()] : [],
            [],
            null,
            "LINUX-01");

    private ScriptedAgentServer Server(AgentRun run)
    {
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer())
            .OnRegister(_ => new AgentRegistrationResult(s_machineId, MachineState.Approved, "session-0", "resume-0", 10, null))
            .OnNext(_ => new AgentNextResult(MachineState.Approved, "session-1", "resume", 10, null, Run: run));
        server.AnswerRunReports = (_, token) => new AgentRunReportResult(token, "resume", "run-token-1");

        return server;
    }

    private LinuxBootDryRun Machine(ScriptedAgentServer server)
    {
        AgentLog log = new(new ImmediateTimeProvider(), _console);
        AgentOptions options = new(new Uri("https://ddt.example:8443/"), null, DryRun: true, DryRunId: 7, NoUpdate: false, KeyboardLayout: null)
        {
            DryRunEnvironment = AgentEnvironment.LinuxBoot,
        };

        return new(
            new DryRunMachineOptions(options, _root, "ddt-agent", Timeout.InfiniteTimeSpan, TestAgents.Version),
            server,
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
    public async Task RegistersAsTheLinuxBootEnvironmentAndWritesTheImageWithItsSeed()
    {
        ScriptedAgentServer server = Server(Run(s_script, s_write, s_seed));

        int exitCode = await Machine(server).RunAsync(server.Stop.Token);

        Assert.Equal(AgentExitCodes.Deployed, exitCode);
        Assert.Equal(AgentEnvironment.LinuxBoot, Assert.Single(server.Registrations).Environment);
        Assert.Equal(4, server.Registrations[0].SequenceVersion);
        Assert.All(server.RunReports, report => Assert.Equal(SequencePhase.LinuxBoot, report.Phase));
        Assert.Equal(DeploymentState.Done, server.RunReports[^1].State);
        Assert.Equal(Enumerable.Repeat(StepState.Done, 3), server.RunReports[^1].Steps.Select(step => step.State));

        // The root held the script and is gone with the run. The disk stays to be looked at.
        Assert.False(Directory.Exists(_root));
        using FileStream disk = new(FileRawDisks.PathFor(_root, 0), FileMode.Open, FileAccess.Read, FileShare.Read);
        byte[] head = new byte[GptLayout.MaxHeadBytes];
        disk.ReadExactly(head);
        Assert.Equal(DryRunDisks.Disk.SizeBytes, disk.Length);
        Assert.Equal(["EFI", "root", CloudInitSeed.Label], GptLayout.Read(head).Partitions.Select(partition => partition.Name));
    }

    // These lines, in the order the run went, each in the boot environment's own words.
    [Fact]
    public async Task SaysWhatTheLinuxBootEnvironmentWouldDo()
    {
        ScriptedAgentServer server = Server(Run(s_script, s_write, s_seed));
        string scripts = Path.Combine(_root, "scripts");

        Assert.Equal(AgentExitCodes.Deployed, await Machine(server).RunAsync(server.Stop.Token));

        string[] expected =
        [
            "Dry run: the fake machine started the Linux boot environment, which runs the sequences that write a raw disk image.",
            $"Dry run: {DryRunDisks.Disk.Describe()} stands in for this computer's disks.",

            // The disk is opened before anything runs, to see that its sectors fit the image.
            $"Dry run: {FileRawDisks.PathFor(_root, 0)} stands in for disk 0.",
            $"Dry run: not run in {scripts}, and taken as exit code 0: {ToolRunner.CommandLine("/bin/bash", [Path.Combine(scripts, $"{s_script.Id:D}.sh")])}",
            "Dry run: no disk is cleaned. On a machine, the partition table of disk 0 and what it held would be erased now.",
            "Dry run: in the Linux boot environment the due restart would be recorded in /run/ddt/restart-due, which the restart clears.",
            "Dry run: this computer is not restarted. In the Linux boot environment, the machine would restart now.",
        ];
        string[] lines = Lines();
        Assert.Equal(expected, lines.Where(expected.Contains).Distinct());
        Assert.DoesNotContain(lines, line => line.Contains("Windows PE", StringComparison.Ordinal) || line.Contains("diskpart", StringComparison.Ordinal));
    }

    // The fake machine started the other boot environment, so nothing is written for a sequence that installs Windows.
    [Fact]
    public async Task RefusesASequenceThatInstallsWindows()
    {
        ScriptedAgentServer server = Server(Run(TestRuns.Partition));

        await Machine(server).RunAsync(server.Stop.Token);

        AgentRunReport failed = server.RunReports[^1];
        Assert.Equal(DeploymentState.Failed, failed.State);
        Assert.Equal(BootEnvironmentCheck.Message(SequenceKind.Windows, AgentEnvironment.LinuxBoot), failed.Error);
        Assert.False(File.Exists(FileRawDisks.PathFor(_root, 0)));
    }
}
