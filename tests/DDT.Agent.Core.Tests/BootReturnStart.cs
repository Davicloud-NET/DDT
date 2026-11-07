// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Contracts.Agents;
using DDT.Contracts.Machines;
using Xunit;

namespace DDT.Agent.Tests;

// A start of the Linux boot environment on a machine whose disk a run seeded, up to where its loop would register:
// the look, with a keyboard, a prompt, a firmware and a server a test scripts and reads.
internal sealed class BootReturnStart : IDisposable
{
    public const string Removed = "What the hand-over put on the cloud-init seed of disk 0 is removed again: the agent, its script and the run's files.";

    public const string RestartsSoon = "The machine restarts into the installed Linux in 10 seconds.";

    public const string RestartsUnlessKey =
        $"{RestartsSoon} To start over here instead, press a key within that time. The agent then asks before it ends the run.";

    public const string RestartsNow = "The machine restarts into the installed Linux.";

    private readonly StringWriter _console = new();

    public BootReturnStart() => Log = new AgentLog(Time, _console);

    public SeededRun Seeded { get; } = new();

    public ImmediateTimeProvider Time { get; } = new();

    public AgentLog Log { get; }

    public RecordingDiskStart DiskStart { get; } = new();

    public FakeKeyWait Keys { get; set; } = new();

    // What someone types once a key stopped the countdown. With no line, nobody answers.
    public ScriptedSignInPrompt Prompt { get; set; } = new();

    // The resume token an agent left in the firmware before the restart, and how often it was taken from there.
    public string? Carried { get; set; }

    public int CarriedTaken { get; private set; }

    public int Restarts { get; private set; }

    // The server's calls up to the restart.
    public List<string> CallsAtRestart { get; } = [];

    public ScriptedAgentServer Server => Seeded.Run.Server;

    public void Dispose()
    {
        Seeded.Dispose();
        _console.Dispose();
    }

    public LinuxBootReturn Return(IDisks? listsForTheLook = null, TimeProvider? time = null, HandedOverQuestionTime? questionTime = null) =>
        new(
            Server,
            new AgentMachine(new DryRunMachineIdentityReader(1), Seeded.Run.Tools, new LocalRunLocator([]), TestAgents.Version, AgentEnvironment.LinuxBoot),
            TestAgents.Status(new ScriptedSignInPrompt { IsAvailable = false }, new AgentLog(Time, TextWriter.Null)),
            Log,
            time ?? Time)
        {
            Seeds = new HandedOverSeeds(Seeded.Run.RawDisks, Log, Time),
            Disks = listsForTheLook,
            StartOver = new StartOverQuestion(Keys, Prompt, Log, Time),
            DiskStart = DiskStart,
            Restart = () =>
            {
                Restarts++;
                CallsAtRestart.AddRange(Server.Calls);

                return Task.CompletedTask;
            },
            Carried = () =>
            {
                CarriedTaken++;

                return Carried;
            },
            Time = questionTime,
        };

    public Task<BootReturnOutcome> RunAsync() => Return().RunAsync(Server.Stop.Token);

    // The server's answer to a question while the run goes on in the installed Linux: no token, and the count.
    public static AgentRegistrationResult GoesOn(int detours = 1) =>
        new(StepRunnerFixture.MachineId, MachineState.Deploying, null, null, 10, null) { InstalledRun = new AgentInstalledRun(StepRunnerFixture.RunId, detours, 5) };

    // The server's answer to a registration as any other, which started the machine over.
    public static AgentRegistrationResult StartedOver(string resumeToken = "resume-0") =>
        new(StepRunnerFixture.MachineId, MachineState.Pending, "poll-0", resumeToken, 10, null);

    // What the screen showed, without each line's time and level.
    public string[] Lines() => [.. _console.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Select(line => line[15..])];

    public void AssertNoLineNamesAToken()
    {
        string said = _console + string.Join('\n', Server.SentLines.Select(line => line.Message));

        Assert.DoesNotContain("run-token", said, StringComparison.Ordinal);
        Assert.DoesNotContain("session-", said, StringComparison.Ordinal);
        Assert.DoesNotContain("resume-", said, StringComparison.Ordinal);
        Assert.DoesNotContain("poll-", said, StringComparison.Ordinal);
    }
}
