// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Consoles;
using DDT.Agent.Deployment;
using DDT.ConsoleProtocol;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Contracts.Sequences;
using DDT.Core.CloudInit;
using DDT.Core.Disks;
using Xunit;

namespace DDT.Agent.Tests;

// A dry run whose machine changes its boot environment, put together the way the Windows program does: the agent
// leaves its resume token in the folder that stands in for the firmware, "restarts", and goes on as the other boot
// environment's agent in the same process, signed in as before.
public sealed class DryRunBootTests : IDisposable
{
    private const string GoesOnThere = "Dry run: the machine restarts into the Linux boot environment, which here starts its agent over there.";

    private static readonly Guid s_machineId = Guid.Parse("0193a4b2-0000-7000-8000-000000000001");

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

    private static readonly AgentSequenceChoice s_linuxChoice = new(
        Guid.Parse("0193a4b2-0000-7000-8000-00000000e004"),
        "Install Linux",
        null,
        ErasesDisk: true,
        NeedsComputerName: false,
        RequiredBytes: 0,
        Suggested: false,
        Environment: AgentEnvironment.LinuxBoot);

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

    private AgentRun LinuxRun() =>
        new(
            TestRuns.RunId,
            DeploymentState.Assigned,
            "Install Linux",
            new SequenceDefinition(SequenceDefinition.CurrentVersion, [s_write, s_seed]),
            [_image.RunImage()],
            [],
            null,
            "LINUX-01");

    private static AgentRegistrationResult Registered(string? signedInBy = null) =>
        new(s_machineId, MachineState.Approved, "session-0", "resume-0", 10, signedInBy);

    private static AgentNextResult Next(string token, AgentRun? run = null, AgentEnvironment? restartInto = null) =>
        new(MachineState.Approved, token, $"resume-of-{token}", 10, null, Run: run, RestartInto: restartInto);

    private DryRunBoot Machine(ScriptedAgentServer server, AgentEnvironment? starts = null, IMachineConsole? console = null)
    {
        AgentLog log = new(new ImmediateTimeProvider(), _console);
        AgentOptions options = new(new Uri("https://ddt.example:8443/"), null, DryRun: true, DryRunId: 7, NoUpdate: false, KeyboardLayout: null)
        {
            DryRunEnvironment = starts,
        };

        return new(
            new DryRunMachineOptions(options, _root, "ddt-agent", Timeout.InfiniteTimeSpan, TestAgents.Version),
            server,
            _ => server,
            TestAgents.Status(console ?? new TextMachineConsole(new ScriptedSignInPrompt { IsAvailable = false }, log)),
            log,
            new ImmediateTimeProvider());
    }

    // What the console showed, without each line's time and level.
    private string[] Lines() =>
        [.. _console.ToString().Split(Environment.NewLine).Select(line => line.Length > 15 ? line[15..] : line)];

    private void AssertTheImageWasWritten()
    {
        using FileStream disk = new(FileRawDisks.PathFor(_root, 0), FileMode.Open, FileAccess.Read, FileShare.Read);
        byte[] head = new byte[GptLayout.MaxHeadBytes];
        disk.ReadExactly(head);
        Assert.Equal(["EFI", "root", CloudInitSeed.Label], GptLayout.Read(head).Partitions.Select(partition => partition.Name));
    }

    // A run assigned on the web: the poll answer names the Linux boot environment, and nobody is at the machine.
    [Fact]
    public async Task AMachineInWindowsPEToldToRestartWritesTheImageAsTheLinuxBootEnvironmentWithoutSigningInAgain()
    {
        AgentRun run = LinuxRun();
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer())
            .OnRegister(_ => Registered())
            .OnNext(_ => Next("session-1", restartInto: AgentEnvironment.LinuxBoot))
            .OnRegister(_ => Registered())
            .OnNext(_ => Next("session-2", run));
        server.AnswerRunReports = (_, token) => new AgentRunReportResult(token, "resume", "run-token-1");

        int exitCode = await Machine(server).RunAsync(server.Stop.Token);

        Assert.Equal(AgentExitCodes.Deployed, exitCode);
        Assert.Equal(
            [(AgentEnvironment.WindowsPE, null), (AgentEnvironment.LinuxBoot, "resume-of-session-1")],
            server.Registrations.Select(registration => (registration.Environment, registration.ResumeToken)));
        Assert.All(server.RunReports, report => Assert.Equal(SequencePhase.LinuxBoot, report.Phase));
        Assert.Equal(DeploymentState.Done, server.RunReports[^1].State);
        AssertTheImageWasWritten();

        // The firmware's stand-in is gone with the rest of the fake machine, and the token was taken out before.
        Assert.False(Directory.Exists(_root));

        string[] expected =
        [
            "The machine restarts into the Linux boot environment, where it stays signed in and goes on.",
            "Dry run: this computer is not restarted. In Windows PE, BootNext would be set to BootCurrent, so the machine starts from the network again, and wpeutil reboot would run now.",
            GoesOnThere,
            "Dry run: the fake machine started the Linux boot environment, which runs the sequences that write a raw disk image.",
        ];
        Assert.Equal(expected, Lines().Where(expected.Contains));
        Assert.DoesNotContain(Lines(), line => line.Contains("could not start the other boot environment", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AMachineInTheLinuxBootEnvironmentToldToRestartInstallsWindowsAsWindowsPE()
    {
        TestImage image = TestImage.Wim();
        AgentRun run = TestRuns.Run([TestRuns.Partition, TestRuns.Apply], image);
        ScriptedAgentServer server = image.Serve(new ScriptedAgentServer())
            .OnRegister(_ => Registered())
            .OnNext(_ => Next("session-1", restartInto: AgentEnvironment.WindowsPE))
            .OnRegister(_ => Registered())
            .OnNext(_ => Next("session-2", run));
        server.AnswerRunReports = (_, token) => new AgentRunReportResult(token, "resume", "run-token-1");

        int exitCode = await Machine(server, AgentEnvironment.LinuxBoot).RunAsync(server.Stop.Token);

        Assert.Equal(AgentExitCodes.Deployed, exitCode);
        Assert.Equal(
            [(AgentEnvironment.LinuxBoot, null), (AgentEnvironment.WindowsPE, "resume-of-session-1")],
            server.Registrations.Select(registration => (registration.Environment, registration.ResumeToken)));
        Assert.All(server.RunReports, report => Assert.Equal(SequencePhase.WindowsPE, report.Phase));
        Assert.Equal(DeploymentState.Done, server.RunReports[^1].State);

        string[] expected =
        [
            "The machine restarts into Windows PE, where it stays signed in and goes on.",
            "Dry run: this computer is not restarted. In the Linux boot environment, BootNext would be set to BootCurrent, so the machine starts from the network again, and the machine would restart now.",
            "Dry run: the machine restarts into Windows PE, which here starts its agent over there.",
        ];
        Assert.Equal(expected, Lines().Where(expected.Contains));
        Assert.Single(Lines(), line => line.StartsWith("Dry run: wimlib is not run. It would apply image 1 (Windows 11 Pro", StringComparison.Ordinal));
    }

    // The technician picks the Linux sequence in Windows PE. Nothing but the list is asked there. After the restart the
    // sequence comes first, is picked again, and only then the disk is confirmed and the run made.
    [Fact]
    public async Task ASequencePickedInWindowsPEIsPickedAgainAfterTheRestartAndRunsThere()
    {
        AgentRun run = LinuxRun();
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer())
            .OnRegister(_ => Registered("bob"))
            .OnNext(_ => Next("session-1") with { CanPickSequence = true })
            .OnSequences(() => [s_linuxChoice])
            .OnEnvironment(_ => { })
            .OnRegister(_ => Registered("bob"))
            .OnNext(_ => Next("session-2") with { CanPickSequence = true, SuggestedSequenceId = s_linuxChoice.Id })
            .OnSequences(() => [s_linuxChoice with { Suggested = true }])

            // The agent polls between two questions, here between the list and ERASE.
            .OnNext(_ => Next("session-3") with { CanPickSequence = true, SuggestedSequenceId = s_linuxChoice.Id })
            .OnPickSequence(_ => run)
            .OnNext(_ => Next("session-4", run));
        server.AnswerRunReports = (_, token) => new AgentRunReportResult(token, "resume", "run-token-1");
        ScriptedMachineConsole console = new(
            ScriptedMachineConsole.Sequence("Install Linux"),
            ScriptedMachineConsole.Sequence("Install Linux"),
            ScriptedMachineConsole.Typed("ERASE"));

        int exitCode = await Machine(server, console: console).RunAsync(server.Stop.Token);

        Assert.Equal(AgentExitCodes.Deployed, exitCode);
        Assert.Equal(new AgentEnvironmentRequest(AgentEnvironment.LinuxBoot, s_linuxChoice.Id), Assert.Single(server.EnvironmentRequests));
        Assert.Equal(
            [(AgentEnvironment.WindowsPE, null), (AgentEnvironment.LinuxBoot, "resume-of-session-1")],
            server.Registrations.Select(registration => (registration.Environment, registration.ResumeToken)));
        Assert.Equal(new AgentRunRequest(s_linuxChoice.Id, 0, null), Assert.Single(server.RunRequests));
        Assert.Equal(DeploymentState.Done, server.RunReports[^1].State);
        AssertTheImageWasWritten();

        // Before the restart the list alone, with the note. After it the same sequence without the note, then ERASE.
        Assert.Equal([typeof(SequenceQuestion), typeof(SequenceQuestion), typeof(EraseQuestion)], console.Questions.Select(question => question.GetType()));
        SequenceOption before = Assert.Single(((SequenceQuestion)console.Questions[0]).Sequences);
        SequenceOption after = Assert.Single(((SequenceQuestion)console.Questions[1]).Sequences);
        Assert.Equal(("The machine restarts into the Linux boot environment first.", false), (before.Description, before.Suggested));
        Assert.Null(after.Description);
        Assert.True(after.Suggested);
        Assert.Contains(GoesOnThere, Lines());
    }
}
