// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using DDT.Agent.Deployment;
using DDT.Agent.InstalledPhase;
using DDT.Agent.LinuxPhase;
using DDT.Agent.Sequences;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Contracts.Sequences;
using DDT.Core.CloudInit;
using DDT.Core.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

// An installed Linux as the vendor-data script leaves it, under a folder that stands in for /, and the agent's loop
// over it. Tools and mounts are faked, the files are real. Another folder stands in for the mounted seed.
internal sealed class LinuxPhaseFixture : IDisposable
{
    public static readonly Guid MachineId = Guid.Parse("0193a4b2-0000-7000-8000-000000000001");

    public static readonly WriteRawImageStep Write = new() { Id = Guid.Parse("0193a4b2-0000-7000-8000-00000000e001"), Name = "Write the disk", ImageId = TestRawImage.ImageId };

    public static readonly WriteCloudInitSeedStep SeedStep = new()
    {
        Id = Guid.Parse("0193a4b2-0000-7000-8000-00000000e002"),
        Name = "Seed",
        MetaData = "instance-id: a\n",
        UserData = "#cloud-config\n",
    };

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ddt-linux-phase-{Guid.NewGuid():N}");

    public LinuxPhaseFixture()
    {
        Paths = new LinuxAgentPaths(Path.Combine(_root, "system"));
        Tools.Answer = (fileName, arguments) =>
        {
            // systemd stops the unit on the machine's way down.
            if (fileName == LinuxPhaseRebooter.Systemctl && arguments is ["reboot"])
            {
                Stopping?.Cancel();
            }

            return [];
        };
    }

    public LinuxAgentPaths Paths { get; }

    public RecordingToolRunner Tools { get; } = new();

    public FakeMounts Mounts { get; } = new();

    public StringWriter Console { get; } = new();

    // What a restart the agent asks for stops, as systemd stops the unit.
    public CancellationTokenSource? Stopping { get; set; }

    public string Seed => Path.Combine(_root, "seed");

    // Where the run keeps its scripts, packages and files.
    public string RunDirectory => RunFiles.DirectoryIn(Paths.State);

    public RunFiles Files => RunFiles.In(Paths.State, new AgentLog(new ImmediateTimeProvider(), TextWriter.Null));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        Console.Dispose();
    }

    // A shell script in the installed Linux whose id ends in number, with the web's default restart code.
    public static RunScriptStep Script(int number) => new()
    {
        Id = Guid.Parse($"0193a4b2-0000-7000-8000-00000000c{number:D3}"),
        Name = $"Script {number}",
        Phase = SequencePhase.Linux,
        Interpreter = ScriptInterpreter.Shell,
        Script = $"echo {number}",
        RebootExitCodes = [194],
    };

    // The boot environment's two steps, then these.
    public static AgentRun Run(params SequenceStep[] inLinux) => Run(inLinux, []);

    public static AgentRun Run(IReadOnlyList<SequenceStep> inLinux, IReadOnlyList<AgentRunPackage> packages) =>
        new(
            TestRuns.RunId,
            DeploymentState.Running,
            "Install Linux",
            new SequenceDefinition(SequenceDefinition.CurrentVersion, [Write, SeedStep, .. inLinux]),
            [],
            packages,
            null,
            "LINUX-01");

    public static AgentRegistrationResult Continued() =>
        new(MachineId, MachineState.Deploying, "session-0", "resume-0", 10, null, TestRuns.RunId, "run-token-2");

    public static AgentNextResult Next(string token, AgentRun? run) =>
        new(MachineState.Deploying, token, "resume", 10, null, Run: run);

    // What the hand-over and the script leave: the run at its first step in Linux with its token, the agent's settings,
    // its program and unit, and the seed without the token.
    public async Task HandOverAsync(AgentRun run)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        IReadOnlyList<SequenceStep> steps = run.Sequence.Steps;
        SequenceState state = SequenceStates.Start(run.Id, run.Sequence) with
        {
            Phase = SequencePhase.Linux,
            Steps = [.. SequenceTree.Nodes(run.Sequence).Select((step, index) => new StepRunState(step.Id, index < 2 ? StepState.Done : StepState.Pending, null, index < 2 ? 1 : 0))],
            Variables = new Dictionary<string, string>(StringComparer.Ordinal) { [RunVariables.RawImageWritten] = RunVariables.Set },
            Cursor = steps.Count > 2 ? new NodeCursor(steps[2].Id, false) : null,
        };
        await Files.SaveStateAsync(state, cancellationToken);
        await Files.SaveTokenAsync("run-token-1", cancellationToken);
        Put(Paths.Configuration, "{\"serverUrl\":\"https://ddt.example:8443/\"}");
        Put(Paths.Agent, "the agent");
        Put(Paths.Unit, "[Unit]");
        Put(Path.Combine(Seed, CloudInitSeed.UserData), SeedStep.UserData);
        Put(Path.Combine(Seed, CloudInitSeed.VendorData), VendorDataScript.Text);
        Put(Path.Combine(Seed, LinuxAgentFiles.SeedAgent), "the agent");
        Put(Path.Combine(Seed, LinuxAgentFiles.SeedState), "{}");
    }

    public void Put(string file, string content = "")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, content);
    }

    public InstalledPhaseLoop Loop(IAgentServer server, TimeProvider? timeProvider = null)
    {
        TimeProvider time = timeProvider ?? new ImmediateTimeProvider();
        AgentLog log = new(time, Console);

        return new LinuxPhaseLoopBuilder
        {
            Server = server,
            IdentityReader = new DryRunMachineIdentityReader(1),
            Paths = Paths,
            Tools = Tools,
            Scripts = new ShellScriptLauncher(dryRun: true)
            {
                Environment = new Dictionary<string, string>(StringComparer.Ordinal) { ["PATH"] = "/usr/sbin:/usr/bin", ["HOME"] = "/root" },
            },
            Shares = new LinuxShareConnector(Mounts, Paths.Shares, log) { Resolve = (_, _) => Task.FromResult<IPAddress[]>([IPAddress.Parse("10.0.2.2")]) },
            Seeds = new FolderSeeds(Seed),
            CloudInit = new CloudInitFiles(Paths.Root),
            RestartMarker = new LinuxRestartMarker(Paths.Run, log),
            FreeSpace = new FakeFreeSpace(),
            Log = log,
            TimeProvider = time,
            AgentVersion = TestAgents.Version,
            HeartbeatInterval = Timeout.InfiniteTimeSpan,
        }.Build();
    }

    // One start of the unit, which a restart the agent asks for stops.
    public Task<int> RunAsync(ScriptedAgentServer server, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(server);

        Stopping = server.Stop;

        return Loop(server, timeProvider).RunAsync(server.Stop.Token);
    }

    // What the log got, without each line's time and level.
    public string[] Lines() =>
        [.. Console.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Select(line => line[15..])];

    // Whether anything of the agent is left where the script and the hand-over put it.
    public bool IsRemoved() =>
        !Directory.Exists(Paths.State)
        && !Directory.Exists(Paths.Program)
        && !File.Exists(Paths.Unit)
        && !Directory.Exists(Path.Combine(Seed, LinuxAgentFiles.SeedFolder))
        && !File.Exists(Path.Combine(Seed, CloudInitSeed.VendorData))
        && File.Exists(Path.Combine(Seed, CloudInitSeed.UserData));
}
