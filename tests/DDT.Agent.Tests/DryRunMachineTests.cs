// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Agent.WindowsPhase;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Contracts.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

// A dry run like the agent runs it with --dry-run: the whole run in one process, from WinPE through the hand-over into
// the installed Windows and on to the agent's removal, against a scripted server.
public sealed class DryRunMachineTests : IDisposable
{
    private static readonly Guid s_machineId = Guid.Parse("0193a4b2-0000-7000-8000-000000000001");

    private static readonly RebootStep s_restartWindows = new() { Id = Guid.Parse("0193a4b2-0000-7000-8000-00000000b005"), Name = "Restart Windows" };

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ddt-dry-run-test-{Guid.NewGuid():N}");
    private readonly string _agent = Path.Combine(Path.GetTempPath(), $"ddt-dry-run-agent-{Guid.NewGuid():N}.exe");
    private readonly X509Certificate2 _rootCertificate;
    private readonly AgentOptions _options;
    private readonly StringWriter _console = new();
    private readonly List<AgentOptions> _connected = [];

    public DryRunMachineTests()
    {
        using ECDsa key = ECDsa.Create();
        using X509Certificate2 root = new CertificateRequest("CN=DDT dry run test root", key, HashAlgorithmName.SHA256)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        _rootCertificate = X509Certificate2.CreateFromPem(root.ExportCertificatePem());
        _options = new AgentOptions(new Uri("https://ddt.example:8443/"), _rootCertificate, DryRun: true, DryRunId: 7, NoUpdate: false, KeyboardLayout: null);
        File.WriteAllBytes(_agent, [0x4D, 0x5A, 0x90, 0x00]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        File.Delete(_agent);
        _rootCertificate.Dispose();
        _console.Dispose();

        foreach (AgentOptions staged in _connected)
        {
            staged.RootCertificate?.Dispose();
        }
    }

    private string Windows => Path.Combine(_root, "W");

    [Fact]
    public async Task RunsTheWholeRunThroughBothPhasesAndLeavesNothing()
    {
        RunScriptStep inWindowsPE = TestRuns.Script(1);
        RunScriptStep inWindows = TestRuns.Script(2, SequencePhase.Windows);
        TestImage image = TestImage.Wim();
        AgentRun assigned = TestRuns.Run(
            [TestRuns.Partition, inWindowsPE, TestRuns.Reboot, TestRuns.Apply, TestRuns.Unattend, inWindows, s_restartWindows, TestRuns.Join],
            image);
        ScriptedAgentServer server = ServerForTheWholeRun(image, assigned);

        int exitCode = await Machine(server).RunAsync(server.Stop.Token);

        Assert.Equal(AgentExitCodes.Deployed, exitCode);
        Assert.False(Directory.Exists(_root));

        Assert.Equal(
            [
                (AgentEnvironment.WindowsPE, null),
                (AgentEnvironment.WindowsPE, "run-token-1"),
                (AgentEnvironment.Windows, "run-token-1"),
                (AgentEnvironment.Windows, "run-token-1"),
                (AgentEnvironment.Windows, "run-token-1"),
            ],
            server.Registrations.Select(registration => (registration.Environment, registration.RunToken)));

        // Each start of Windows reached the server through the agent.json the hand-over staged.
        Assert.Equal(3, _connected.Count);
        Assert.All(_connected, staged => Assert.Equal(
            (_options.ServerUrl, _rootCertificate.Thumbprint),
            (staged.ServerUrl, staged.RootCertificate?.Thumbprint)));

        AgentRunReport done = server.RunReports[^1];
        Assert.Equal((DeploymentState.Done, SequencePhase.Windows), (done.State, done.Phase));
        Assert.Equal(Enumerable.Repeat(StepState.Done, 8), done.Steps.Select(step => step.State));
        Assert.Single(server.Calls, call => call.StartsWith($"run-credentials {TestRuns.Join.Id}", StringComparison.Ordinal));

        // These lines, in the order the run went.
        string[] lines = Lines();
        string[] expected = LinesOfTheWholeRun(inWindowsPE, inWindows);
        Assert.Equal(expected, lines.Where(expected.Contains));
        Assert.Single(lines, line => line.StartsWith("Dry run: wimlib is not run. It would apply image 1 (Windows 11 Pro", StringComparison.Ordinal));
        Assert.Equal(3, lines.Count(line => line.StartsWith("Dry run: Windows setup counts as finished.", StringComparison.Ordinal)));

        // The join account's password reaches neither the console nor the server's log or reports.
        string password = TestRuns.JoinAccount.Password;
        Assert.DoesNotContain(password, _console.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(TestRuns.JoinAccount.UserName, _console.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(server.SentLines, line => line.Message.Contains(password, StringComparison.Ordinal));
        Assert.All(server.RunReports, report => Assert.DoesNotContain(password, report.Error ?? string.Empty, StringComparison.Ordinal));
    }

    // Like when the dry run's process ended and is started again with the same dry run id. A new machine on the same
    // root continues in the phase the run's state names, with the server the staged agent.json names.
    [Fact]
    public async Task ADryRunStartedAgainGoesOnInWindowsWhereItStopped()
    {
        AgentRun run = await StopInWindowsAsync();
        await File.WriteAllTextAsync(StagedConfiguration, """{"serverUrl":"https://ddt-moved.example/"}""", TestContext.Current.CancellationToken);

        ScriptedAgentServer second = new ScriptedAgentServer()
            .OnRegister(registration => Registered() with { RunId = TestRuns.RunId, RunToken = registration.RunToken })
            .OnNext(_ => Next("session-2", run with { State = DeploymentState.Running }));

        Assert.Equal(AgentExitCodes.Deployed, await Machine(second).RunAsync(second.Stop.Token));

        AgentRegistration registration = Assert.Single(second.Registrations);
        Assert.Equal((AgentEnvironment.Windows, "run-token-1"), (registration.Environment, registration.RunToken));
        Assert.Equal(new Uri("https://ddt-moved.example/"), _connected[^1].ServerUrl);
        Assert.Null(_connected[^1].RootCertificate);
        Assert.Equal(DeploymentState.Done, second.RunReports[^1].State);
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public async Task TheServiceDoesNotStartWithoutTheStagedAgentJson()
    {
        await StopInWindowsAsync();
        File.Delete(StagedConfiguration);
        ScriptedAgentServer second = new();

        Assert.Equal(AgentExitCodes.ConfigurationError, await Machine(second).RunAsync(second.Stop.Token));
        Assert.Empty(second.Registrations);
        Assert.Contains($"ERROR Dry run: the DdtSequence service cannot start: Cannot read {StagedConfiguration}", _console.ToString(), StringComparison.Ordinal);
        Assert.True(File.Exists(RunFiles.StatePathIn(Windows)));
    }

    private string StagedConfiguration => Path.Combine(Windows, "DDT", WindowsHandOver.AgentDirectory, WindowsHandOver.ConfigurationFileName);

    // Runs a sequence with a step in Windows until the service registers. The server doesn't answer that. Returns the
    // run.
    private async Task<AgentRun> StopInWindowsAsync()
    {
        TestImage image = TestImage.Wim();
        AgentRun assigned = TestRuns.Run([TestRuns.Partition, TestRuns.Apply, TestRuns.Script(2, SequencePhase.Windows)], image);
        ScriptedAgentServer first = image.Serve(new ScriptedAgentServer())
            .OnRegister(_ => Registered())
            .OnNext(_ => Next("session-1", assigned));
        first.AnswerRunReports = (_, token) => new AgentRunReportResult(token, "resume", "run-token-1");

        Assert.Equal(AgentExitCodes.Stopped, await Machine(first).RunAsync(first.Stop.Token));
        Assert.Equal(AgentEnvironment.Windows, first.Registrations[^1].Environment);
        Assert.Equal(SequencePhase.Windows, (await RunFiles.In(Windows, Log()).LoadStateAsync(TestContext.Current.CancellationToken))?.Phase);

        return assigned;
    }

    // WinPE starts twice, around the restart step, and Windows three times: after the hand-over, after the restart step
    // and after the join.
    private static ScriptedAgentServer ServerForTheWholeRun(TestImage image, AgentRun assigned)
    {
        AgentRun running = assigned with { State = DeploymentState.Running };
        ScriptedAgentServer server = image.Serve(new ScriptedAgentServer()).OnRunCredentials(_ => TestRuns.JoinAccount);
        server.OnRegister(_ => Registered()).OnNext(_ => Next("session-1", assigned));

        for (int start = 2; start <= 5; start++)
        {
            string session = $"session-{start}";
            server.OnRegister(registration => Registered() with { RunId = TestRuns.RunId, RunToken = registration.RunToken })
                .OnNext(_ => Next(session, running));
        }

        server.AnswerRunReports = (_, token) => new AgentRunReportResult(token, "resume", "run-token-1");

        return server;
    }

    // What the console says about the whole run, among its other lines.
    private string[] LinesOfTheWholeRun(RunScriptStep inWindowsPE, RunScriptStep inWindows)
    {
        string scripts = Path.Combine(Windows, "DDT", "scripts");
        string restart = "Dry run: the machine restarts, which here starts its agent over.";
        string service = $"Dry run: Windows starts the DdtSequence service, which here goes on in this process with {Path.Combine(Windows, "DDT", "agent", "agent.json")}.";
        string shutdown = $"Dry run: not run: {ToolRunner.CommandLine(WindowsRebooter.ShutdownPath, WindowsRebooter.Arguments)}";

        return
        [
            $"Dry run: not run in {scripts}, and taken as exit code 0: {ToolRunner.CommandLine(RunScriptStepRunner.CmdPath, ["/d", "/c", Path.Combine(scripts, $"{inWindowsPE.Id:D}.cmd")])}",
            "Dry run: this computer is not restarted. In Windows PE, BootNext would be set to BootCurrent, so the machine starts from the network again, and wpeutil reboot would run now.",
            restart,
            $"The agent is in {Path.Combine(Windows, "DDT", "agent")}, to go on with the run in Windows.",
            "Dry run: this computer is not restarted. In Windows PE, wpeutil reboot would run now.",
            restart,
            service,
            $"Deleted the answer file {UnattendFile.PathIn(Windows)}, which holds passwords.",
            $"Dry run: not run in {scripts}, and taken as exit code 0: {ToolRunner.CommandLine(RunScriptStepRunner.CmdPath, ["/d", "/c", Path.Combine(scripts, $"{inWindows.Id:D}.cmd")])}",
            shutdown,
            restart,
            service,
            "Dry run: this computer does not join corp.example.test. In Windows, NetJoinDomain would join it with the account the server sent.",
            shutdown,
            restart,
            service,
            "The run is over here, so the agent removes itself.",
            $"Dry run: not run: {ToolRunner.CommandLine(AgentRemoval.ScPath, ["delete", OfflineServiceRegistration.ServiceName])}",
            $"Dry run: in Windows, {Path.Combine(Windows, "DDT", "agent", "ddt-agent.exe")} would be marked for deletion when Windows next starts.",
            $"Dry run: in Windows, {Path.Combine(Windows, "DDT")} would be marked for deletion when Windows next starts.",
            shutdown,
        ];
    }

    private static AgentLog Log() => new(new ImmediateTimeProvider(), TextWriter.Null);

    private static AgentRegistrationResult Registered() =>
        new(s_machineId, MachineState.Approved, "session-0", "resume-0", 10, null);

    private static AgentNextResult Next(string token, AgentRun run) =>
        new(MachineState.Approved, token, "resume", 10, null, Run: run);

    // What the console showed, without each line's time and level.
    private string[] Lines() =>
        [.. _console.ToString().Split(Environment.NewLine).Select(line => line.Length > 15 ? line[15..] : line)];

    private DryRunMachine Machine(ScriptedAgentServer server)
    {
        AgentLog log = new(new ImmediateTimeProvider(), _console);

        return new(
            new DryRunMachineOptions(_options, _root, _agent, Timeout.InfiniteTimeSpan, TestAgents.Version),
            server,
            staged =>
            {
                _connected.Add(staged);

                return server;
            },
            TestAgents.Status(new ScriptedSignInPrompt { IsAvailable = false }, log),
            log,
            new ImmediateTimeProvider());
    }
}
