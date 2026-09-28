// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

// The dry run's pieces with the engine across a Windows PE restart, as the agent puts them together: the run's state
// reaches the dry run's Windows directory at Partition, the restart only says what it would do, and new instances that
// share nothing with the first find the run there and go on after the restart without partitioning again.
public sealed class DryRunResumeTests : IDisposable
{
    private static readonly Guid s_machineId = Guid.Parse("0193a4b2-0000-7000-8000-000000000001");
    private static readonly Guid s_runId = Guid.Parse("0193a4b2-0000-7000-8000-0000000000f1");

    private static readonly SequenceDefinition s_definition = new(
        SequenceDefinition.CurrentVersion,
        [
            new PartitionStep { Id = Guid.Parse("0193a4b2-0000-7000-8000-0000000000a1"), Name = "Partition" },
            Script("0193a4b2-0000-7000-8000-0000000000a2", "Before the restart"),
            new RebootStep { Id = Guid.Parse("0193a4b2-0000-7000-8000-0000000000a3"), Name = "Restart" },
            Script("0193a4b2-0000-7000-8000-0000000000a4", "After the restart"),
            Script("0193a4b2-0000-7000-8000-0000000000a5", "Only on an OptiPlex") with
            {
                Conditions = [new StepCondition(MachineVariableNames.Model, ConditionOperator.StartsWith, "OptiPlex")],
            },
        ]);

    private static readonly AgentRun s_run = new(s_runId, DeploymentState.Running, "Dry run", s_definition, [], [], 0, null);

    private static readonly MachineVariables s_machine =
        new("Dell Inc.", "Latitude 5440", "SN-1", "4c4c4544-0042-3510-8052-b4c04f4d3232", ["00155D010203"], null, SequencePhase.WindowsPE);

    private static readonly MachineIdentity s_identity = new DryRunMachineIdentityReader(1).Read();

    private static readonly RepeatStep s_twice = new()
    {
        Id = Guid.Parse("0193a4b2-0000-7000-8000-0000000000b6"),
        Name = "Twice with a restart",
        Steps = [Script("0193a4b2-0000-7000-8000-0000000000b7", "Before each restart"), new RebootStep { Id = Guid.Parse("0193a4b2-0000-7000-8000-0000000000b8"), Name = "Restart" }],
        Until = new TestCondition("Label", ConditionOperator.Equals, "never"),
        MaxTimes = 2,
        GoOnAtLimit = true,
    };

    private static readonly PauseStep s_pause = new()
    {
        Id = Guid.Parse("0193a4b2-0000-7000-8000-0000000000b9"),
        Name = "Check the BIOS",
        Message = "Check the BIOS of {{Label}}.",
    };

    // A name worked out from the serial number, an IF on the model that takes the dry run's branch, a repeat that
    // restarts the machine each time through, and a pause that the web continues.
    private static readonly SequenceDefinition s_tree = new(
        SequenceDefinition.CurrentVersion,
        [
            new PartitionStep { Id = Guid.Parse("0193a4b2-0000-7000-8000-0000000000b1"), Name = "Partition" },
            new SetVariableStep { Id = Guid.Parse("0193a4b2-0000-7000-8000-0000000000b2"), Name = "Name the PC", Variable = "Label", Value = "PC-{{SerialNumber|alnum}}" },
            new IfStep
            {
                Id = Guid.Parse("0193a4b2-0000-7000-8000-0000000000b3"),
                Name = "By model",
                Test = new TestCondition(MachineVariableNames.Model, ConditionOperator.Equals, s_identity.Model!),
                Then = [Script("0193a4b2-0000-7000-8000-0000000000b4", "On the dry run")],
                Else = [Script("0193a4b2-0000-7000-8000-0000000000b5", "Elsewhere")],
            },
            s_twice,
            s_pause,
            Script("0193a4b2-0000-7000-8000-0000000000ba", "Last"),
        ])
    {
        Variables = [new VariableDeclaration { Name = "Label", SetBySteps = true }],
    };

    private static readonly AgentRun s_treeRun = new(s_runId, DeploymentState.Running, "Tree", s_tree, [], [], 0, null);

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ddt-dry-run-test-{Guid.NewGuid():N}");
    private readonly ImmediateTimeProvider _time = new();
    private readonly ScriptedAgentServer _server = new();

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task GoesOnAfterARestartFromTheStateOnTheDisk()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        // The first start of Windows PE.
        AgentLog firstLog = new(_time, TextWriter.Null);
        DryRunDiskPartitioner firstDisk = new(_root, firstLog);
        RunSession firstSession = new(s_machineId, s_run, new DeploymentTokens("session", "resume", "run-token-1"))
        {
            Disk = (await firstDisk.ListDisksAsync(cancellationToken))[0],
        };
        FileRunStateStore firstStore = new(firstSession.Tokens);

        SequenceRunResult first = await Engine(firstSession, firstStore, firstDisk, firstLog)
            .RunAsync(SequenceStates.Start(s_runId, s_definition), s_machine, cancellationToken);
        await new DryRunRebooter(firstLog).RebootAsync(RestartInto.WindowsPE, cancellationToken);

        Assert.Equal(SequenceOutcome.RebootRequired, first.Outcome);
        Assert.Equal(3, first.State.NextIndex);

        // The second start: only the disk remembers the run.
        AgentLog secondLog = new(_time, TextWriter.Null);
        DryRunDiskPartitioner secondDisk = new(_root, secondLog);
        string? windows = new LocalRunLocator([Path.Combine(_root, "W")]).Find();
        Assert.NotNull(windows);

        RunFiles files = RunFiles.In(windows, secondLog);
        SequenceState? saved = await files.LoadStateAsync(cancellationToken);
        Assert.NotNull(saved);
        Assert.Equal("run-token-1", await files.LoadTokenAsync(cancellationToken));

        RunDiskIds? ids = RunVariables.DiskIds(saved.Variables);
        Assert.NotNull(ids);
        RunSession secondSession = new(s_machineId, s_run, new DeploymentTokens("session-2", "resume-2", "run-token-1"))
        {
            Volumes = await secondDisk.FindAsync(ids, windows, cancellationToken),
        };
        FileRunStateStore secondStore = new(secondSession.Tokens);
        await secondStore.AttachAsync(files, cancellationToken);

        SequenceRunResult second = await Engine(secondSession, secondStore, secondDisk, secondLog).RunAsync(saved, s_machine, cancellationToken);

        Assert.Equal(SequenceOutcome.Completed, second.Outcome);
        Assert.Equal(
            [StepState.Done, StepState.Done, StepState.Done, StepState.Done, StepState.Skipped],
            second.State.Steps.Select(step => step.State));
        Assert.Equal(5, (await files.LoadStateAsync(cancellationToken))?.NextIndex);

        List<string> before = await MessagesAsync(firstLog);
        List<string> after = await MessagesAsync(secondLog);
        Assert.Contains(before, line => line.StartsWith("Dry run: diskpart is not run. It would get this script", StringComparison.Ordinal));
        Assert.Contains(before, line => line.Contains("BootNext would be set to BootCurrent", StringComparison.Ordinal));
        Assert.Single(before, line => line.StartsWith("Dry run: not run", StringComparison.Ordinal) && line.EndsWith("a2.cmd", StringComparison.Ordinal));
        Assert.DoesNotContain(after, line => line.StartsWith("Dry run: diskpart is not run. It would get this script", StringComparison.Ordinal));
        Assert.Single(after, line => line.StartsWith("Dry run: not run", StringComparison.Ordinal));
        Assert.Contains(after, line => line.StartsWith("Dry run: not run", StringComparison.Ordinal) && line.EndsWith("a4.cmd", StringComparison.Ordinal));
    }

    // The agent of Windows PE as a dry run puts it together, started twice: the first start ends with the restart's exit
    // code, and a second one, which shares nothing with the first but the dry run's root, finds the run there and
    // finishes it, which removes the root.
    [Fact]
    public async Task TheAgentStartedAgainGoesOnWithTheRun()
    {
        AgentRun assigned = s_run with { State = DeploymentState.Assigned, DiskNumber = null };
        _server.OnRegister(_ => Registered())
            .OnNext(_ => Next("session-1", assigned))
            .OnRunReport(DeploymentState.Running, _ => new AgentRunReportResult("session-2", "resume-2", "run-token-1"));

        // The consoles, as the server only gets what the agents flushed.
        using StringWriter firstConsole = new();
        int first = await Agent(new AgentLog(_time, firstConsole)).RunAsync(_server.Stop.Token);

        Assert.Equal(AgentExitCodes.Restarting, first);
        Assert.True(File.Exists(RunFiles.StatePathIn(Path.Combine(_root, "W"))));

        // Its end was the restart, so the next start does not restart again.
        Assert.False(File.Exists(Path.Combine(_root, WindowsPERestartMarker.FileName)));

        _server.OnRegister(registration => Registered() with { RunId = s_runId, RunToken = registration.RunToken })
            .OnNext(_ => Next("session-3", s_run with { DiskNumber = null }));

        using StringWriter secondConsole = new();
        int second = await Agent(new AgentLog(_time, secondConsole)).RunAsync(_server.Stop.Token);

        Assert.Equal(AgentExitCodes.Deployed, second);
        Assert.Equal("run-token-1", _server.Registrations[1].RunToken);
        Assert.False(Directory.Exists(_root));

        // Each console line starts with the time and the level.
        string[] before = [.. firstConsole.ToString().Split(Environment.NewLine).Select(line => line.Length > 15 ? line[15..] : line)];
        string[] after = [.. secondConsole.ToString().Split(Environment.NewLine).Select(line => line.Length > 15 ? line[15..] : line)];
        Assert.Contains(before, line => line.Contains("BootNext would be set to BootCurrent", StringComparison.Ordinal));
        Assert.Contains(before, line => line.StartsWith(@"Dry run: in Windows PE the due restart would be recorded in X:\DDT\restart-due", StringComparison.Ordinal));
        Assert.DoesNotContain(after, line => line.StartsWith("Dry run: diskpart is not run. It would get this script", StringComparison.Ordinal));
        Assert.Contains(after, line => line.StartsWith("Dry run: not run", StringComparison.Ordinal) && line.EndsWith("a4.cmd", StringComparison.Ordinal));
    }

    // A tree's run across three starts of Windows PE: the first sets a variable, takes the IF's branch for the dry run's
    // model and restarts inside the repeat; the runner goes on from the disk after each restart, a second time through the
    // repeat and a restart, then past the repeat's limit, which lets the run go on, and a pause the web continues, to the
    // end, which removes the dry run's root.
    [Fact]
    public async Task ATreesRunGoesOnAfterRestartsInsideARepeatAndAPauseTheWebContinues()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        _server.AnswerRunReports = (report, token) => report.Activity == RunActivity.Paused
            ? new AgentRunReportResult(token, "resume", "run-token-1", ContinueStepId: s_pause.Id, ContinuePass: 1)
            : new AgentRunReportResult(token, "resume", "run-token-1");

        // The first start, which the engine runs with the dry run's pieces up to the restart inside the repeat.
        AgentLog firstLog = new(_time, TextWriter.Null);
        DryRunDiskPartitioner firstDisk = new(_root, firstLog);
        RunSession firstSession = new(s_machineId, s_treeRun, new DeploymentTokens("session", "resume", "run-token-1"))
        {
            Disk = (await firstDisk.ListDisksAsync(cancellationToken))[0],
        };
        SequenceRunResult first = await Engine(firstSession, new FileRunStateStore(firstSession.Tokens), firstDisk, firstLog)
            .RunAsync(SequenceStates.Start(s_runId, s_tree), RunMachine.Of(s_identity, s_treeRun), cancellationToken);

        Assert.Equal(SequenceOutcome.RebootRequired, first.Outcome);
        Assert.Equal((SequenceState.TreeFormat, new NodeCursor(s_twice.Id, true)), (first.State.Format, first.State.Cursor));
        Assert.Equal("PC-DRYRUN1", first.State.Variables["Label"]);
        Assert.Contains(await MessagesAsync(firstLog), line => line.StartsWith("Dry run: not run", StringComparison.Ordinal) && line.EndsWith("b4.cmd", StringComparison.Ordinal));

        // Two more starts, which only the disk connects: the runner goes on with what it finds there each time.
        RunResult second = await Runner(new AgentLog(_time, TextWriter.Null)).RunAsync(
            s_machineId,
            s_treeRun,
            await LocalRun.LoadAsync(Path.Combine(_root, "W"), new AgentLog(_time, TextWriter.Null), cancellationToken),
            null,
            new DeploymentTokens("session-2", "resume-2", "run-token-1"),
            s_identity,
            cancellationToken);

        Assert.Equal(RunOutcome.Restarting, second.Outcome);

        RunResult third = await Runner(new AgentLog(_time, TextWriter.Null)).RunAsync(
            s_machineId,
            s_treeRun,
            await LocalRun.LoadAsync(Path.Combine(_root, "W"), new AgentLog(_time, TextWriter.Null), cancellationToken),
            null,
            new DeploymentTokens("session-3", "resume-3", "run-token-1"),
            s_identity,
            cancellationToken);

        Assert.Equal(RunOutcome.Finished, third.Outcome);
        Assert.False(Directory.Exists(_root));

        List<string> lines = [.. _server.SentLines.Select(line => line.Message)];
        Assert.Contains("The run of Tree goes on after the steps of Twice with a restart.", lines);
        Assert.Contains(
            "Repeat Twice with a restart runs its steps again (2 of at most 2 times), because its condition to stop did not hold: Label is " +
                "\"never\", and the run's value is \"PC-DRYRUN1\".",
            lines);
        Assert.Contains(lines, line => line.StartsWith("Repeat Twice with a restart ran 2 times, the most it may", StringComparison.Ordinal));
        Assert.Contains("Someone continued the run on the web.", lines);
        // The first time through the repeat was the first start's, which kept its lines to itself.
        Assert.Single(lines, line => line.StartsWith("Dry run: not run", StringComparison.Ordinal) && line.EndsWith("b7.cmd", StringComparison.Ordinal));

        List<AgentRunReport> reports = _server.RunReports;
        AgentRunReport paused = reports.First(report => report.Activity == RunActivity.Paused);
        Assert.Equal((s_pause.Id, "Check the BIOS of PC-DRYRUN1."), (paused.CurrentStepId, paused.PauseMessage));
        Assert.Contains(reports, report => report.Variables?.GetValueOrDefault("Label") == "PC-DRYRUN1");

        AgentRunReport done = reports[^1];
        Assert.Equal(DeploymentState.Done, done.State);
        Assert.Equal(StepState.Skipped, done.Steps.Single(step => step.StepId == Guid.Parse("0193a4b2-0000-7000-8000-0000000000b5")).State);
        Assert.Equal((StepState.Done, 2), done.Steps.Where(step => step.StepId == s_twice.Id).Select(step => (step.State, step.Iteration)).Single());
        Assert.All(done.Steps.Where(step => step.StepId != Guid.Parse("0193a4b2-0000-7000-8000-0000000000b5")), step => Assert.Equal(StepState.Done, step.State));
    }

    // The agent started three times, as the dry run's machine starts Windows PE: each start finds the run where the one
    // before left it, the first through the checks before a run starts.
    [Fact]
    public async Task TheAgentStartedAgainGoesOnWithATreesRun()
    {
        AgentRun assigned = s_treeRun with { State = DeploymentState.Assigned, DiskNumber = null };
        _server.AnswerRunReports = (report, token) => report.Activity == RunActivity.Paused
            ? new AgentRunReportResult(token, "resume", "run-token-1", ContinueStepId: s_pause.Id, ContinuePass: 1)
            : new AgentRunReportResult(token, "resume", "run-token-1");
        _server.OnRegister(_ => Registered())
            .OnNext(_ => Next("session-1", assigned))
            .OnRegister(registration => Registered() with { RunId = s_runId, RunToken = registration.RunToken })
            .OnNext(_ => Next("session-2", s_treeRun with { DiskNumber = null }))
            .OnRegister(registration => Registered() with { RunId = s_runId, RunToken = registration.RunToken })
            .OnNext(_ => Next("session-3", s_treeRun with { DiskNumber = null }));

        int[] exitCodes =
        [
            await Agent(new AgentLog(_time, TextWriter.Null)).RunAsync(_server.Stop.Token),
            await Agent(new AgentLog(_time, TextWriter.Null)).RunAsync(_server.Stop.Token),
            await Agent(new AgentLog(_time, TextWriter.Null)).RunAsync(_server.Stop.Token),
        ];

        Assert.Equal([AgentExitCodes.Restarting, AgentExitCodes.Restarting, AgentExitCodes.Deployed], exitCodes);
        Assert.Equal(["run-token-1", "run-token-1"], _server.Registrations.Skip(1).Select(registration => registration.RunToken));
        Assert.Equal(DeploymentState.Done, _server.RunReports[^1].State);
        Assert.False(Directory.Exists(_root));
    }

    private static AgentRegistrationResult Registered() =>
        new(s_machineId, Contracts.Machines.MachineState.Approved, "session-0", "resume-0", 10, null);

    private static AgentNextResult Next(string token, AgentRun run) =>
        new(Contracts.Machines.MachineState.Approved, token, "resume", 10, null, Run: run);

    private AgentLoop Agent(AgentLog log) =>
        new(
            _server,
            new DryRunMachineIdentityReader(1),
            TestAgents.Status(new ScriptedSignInPrompt { IsAvailable = false }, log),
            new DryRunDiskPartitioner(_root, log),
            Runner(log),
            new LocalRunLocator([Path.Combine(_root, "W")]),
            log,
            _time,
            TestAgents.Version);

    // The runner of Windows PE as a dry run puts it together.
    private SequenceRunner Runner(AgentLog log)
    {
        DryRunToolRunner tools = new(log);

        return new SequenceRunner(
            _server,
            new DryRunDiskPartitioner(_root, log),
            new FileRawDisks(_root, log),
            new DryRunImageApplier(log),
            new DryRunBcdWriter(log),
            new DryRunRebooter(log),
            new WindowsPERestartMarker(_root, log, dryRun: true),
            tools,
            new DryRunDomainJoiner(log),
            new WindowsHandOver(new OfflineServiceRegistration(tools, log, dryRun: true), Environment.ProcessPath!, TestAgents.Configuration, log, dryRun: true),
            log,
            _time,
            Timeout.InfiniteTimeSpan,
            _root,
            Environment.SystemDirectory,
            dryRun: true);
    }

    private static RunScriptStep Script(string id, string name) => new()
    {
        Id = Guid.Parse(id),
        Name = name,
        Phase = SequencePhase.WindowsPE,
        Script = $"echo {name}",
    };

    private SequenceEngine Engine(RunSession session, FileRunStateStore store, DryRunDiskPartitioner disk, AgentLog log)
    {
        DryRunToolRunner tools = new(log);
        RunDownloads downloads = new(_server, session, log, _time, TimeSpan.FromSeconds(10));
        AgentStepRunner steps = new(
            new PartitionStepRunner(disk, session, store, log, dryRun: true),
            new ApplyImageStepRunner(new DryRunImageApplier(log), downloads, session, log),
            new InjectDriversStepRunner(tools, downloads, session, log),
            new WriteUnattendStepRunner(_server, session, _ => Task.CompletedTask, log, _time),
            new JoinDomainStepRunner(new DryRunDomainJoiner(log), _server, session, _ => Task.CompletedTask, log, _time),
            new RunScriptStepRunner(tools, downloads, session, log, _root),
            new WriteRawImageStepRunner(disk, new FileRawDisks(_root, log), downloads, session, log),
            new WriteCloudInitSeedStepRunner(new FileRawDisks(_root, log), session, log, _time),
            new StepAccounts(_server, session, _ => Task.CompletedTask, AccountTools.DryRun(log), log, _time),
            _ => { },
            log,
            _time);

        return new SequenceEngine(steps, store, new Progress<StepPercent>());
    }

    private async Task<List<string>> MessagesAsync(AgentLog log)
    {
        ScriptedAgentServer server = new();

        while (log.QueuedLines > 0)
        {
            await log.FlushAsync(server, s_machineId, "session", TestContext.Current.CancellationToken);
        }

        return [.. server.SentLines.Select(line => line.Message)];
    }
}
