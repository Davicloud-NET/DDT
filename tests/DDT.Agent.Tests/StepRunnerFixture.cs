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

// One run with fakes for everything outside the agent: the disk and wimlib (Tools), the other tools (ToolRunner),
// the server and time. The disk 0 is chosen for the run. Everything on disk lives under Tools.Root, which Dispose
// removes.
internal sealed class StepRunnerFixture : IDisposable
{
    public static readonly Guid MachineId = Guid.Parse("0193a4b2-0000-7000-8000-000000000001");
    public static readonly Guid RunId = Guid.Parse("0193a4b2-0000-7000-8000-0000000000f1");

    public StepRunnerFixture(
        IReadOnlyList<SequenceStep> steps,
        IReadOnlyList<AgentRunImage>? images = null,
        IReadOnlyList<AgentRunPackage>? packages = null)
    {
        AgentRun run = new(
            RunId,
            DeploymentState.Running,
            "Test sequence",
            new SequenceDefinition(SequenceDefinition.CurrentVersion, steps),
            images ?? [],
            packages ?? [],
            null,
            "PC-042");

        Log = new AgentLog(Time, TextWriter.Null);
        Session = new RunSession(MachineId, run, new DeploymentTokens("session", "resume")) { Disk = FakeDeploymentTools.Disk(0) };
        Downloads = new RunDownloads(Server, Session, Log, Time, TimeSpan.FromSeconds(10));
        Directory.CreateDirectory(WorkDirectory);
    }

    public FakeDeploymentTools Tools { get; } = new();

    public RecordingToolRunner ToolRunner { get; } = new();

    public ScriptedAgentServer Server { get; } = new();

    public ImmediateTimeProvider Time { get; } = new();

    public RecordingProgress Progress { get; } = new();

    public AgentLog Log { get; }

    public RunSession Session { get; }

    public RunDownloads Downloads { get; }

    // Stands in for the agent's own directory, X:\DDT in Windows PE.
    public string WorkDirectory => Path.Combine(Tools.Root, "X");

    public PartitionStepRunner Partition => new(Tools, Session, Log, dryRun: true);

    public ApplyImageStepRunner ApplyImage => new(Tools, Downloads, Session, Log);

    public InjectDriversStepRunner InjectDrivers => new(ToolRunner, Downloads, Session, Log);

    public WriteUnattendStepRunner WriteUnattend => new(Server, Session, ReportRunningAsync, Log, Time);

    public RunScriptStepRunner RunScript => new(ToolRunner, Downloads, Session, Log, WorkDirectory);

    public AgentStepRunner Steps => new(Partition, ApplyImage, InjectDrivers, WriteUnattend, RunScript, Log, Time);

    public StepContext Context(SequencePhase phase = SequencePhase.WindowsPE) =>
        new(
            RunId,
            phase,
            new MachineVariables("Dell Inc.", "Latitude 5440", "SN-1", "4c4c4544-0042-3510-8052-b4c04f4d3232", ["00155D010203"], "PC-042", phase),
            new Dictionary<string, string>(),
            Progress);

    // The disk as Partition leaves it: the three volumes and the run's directory.
    public TargetVolumes Partitioned()
    {
        TargetVolumes volumes = Tools.Volumes;
        Directory.CreateDirectory(volumes.System);
        Directory.CreateDirectory(Path.Combine(volumes.Windows, "DDT"));
        Directory.CreateDirectory(volumes.Recovery);
        Session.Volumes = volumes;

        return volumes;
    }

    public async Task<List<AgentLogLine>> SentLinesAsync()
    {
        while (Log.QueuedLines > 0)
        {
            await Log.FlushAsync(Server, MachineId, "session", TestContext.Current.CancellationToken);
        }

        return Server.SentLines;
    }

    public void Dispose() => Tools.Dispose();

    // Stands in for the heartbeat's report of the running step, which the server records in its calls.
    private async Task ReportRunningAsync(CancellationToken cancellationToken) =>
        await Server.ReportRunAsync(
            MachineId,
            Session.Tokens.Token,
            RunId,
            new AgentRunReport(DeploymentState.Running, SequencePhase.WindowsPE, [], null, 0, RunActivity.Step, null),
            cancellationToken);
}
