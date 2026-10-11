// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using DDT.Agent.Consoles;
using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Contracts.Sequences;
using DDT.Core.CloudInit;
using Xunit;

namespace DDT.Agent.Tests;

// What the Linux boot environment does before it registers when a disk holds a run that was handed over: back to the
// disk without a word to the server while the installed Linux has the run, and with the run's token while it has not.
public sealed class LinuxBootReturnTests : IDisposable
{
    private const string NeverTaken =
        "The machine started from the network 3 times after the run was handed over, and each time the run's token was still on " +
        "the cloud-init seed, so the installed Linux never took the run over. The image has no cloud-init or no systemd, the " +
        "user-data keeps cloud-init from running DDT's script, the machine does not start from its disk, or that script failed. " +
        "Look at the machine's own screen while it starts from its disk, and in the installed Linux at /var/log/cloud-init-output.log.";

    private const string RestartsUnlessKey =
        "The machine restarts into the installed Linux in 10 seconds. To start over here instead, press a key within that time: it ends the run.";

    private const string Removed = "What the hand-over put on the cloud-init seed of disk 0 is removed again: the agent, its script and the run's files.";

    private readonly SeededRun _seeded = new();
    private readonly StringWriter _console = new();
    private readonly ImmediateTimeProvider _time = new();
    private readonly AgentLog _log;
    private readonly RecordingDiskStart _diskStart = new();
    private readonly List<string> _callsAtRestart = [];
    private FakeKeyWait _keys = new();
    private int _restarts;

    public LinuxBootReturnTests() => _log = new AgentLog(_time, _console);

    public void Dispose()
    {
        _seeded.Dispose();
        _console.Dispose();
    }

    private ScriptedAgentServer Server => _seeded.Run.Server;

    private LinuxBootReturn Return(IDisks? disks = null) =>
        new(
            Server,
            new AgentMachine(new DryRunMachineIdentityReader(1), disks ?? _seeded.Run.Tools, new LocalRunLocator([]), TestAgents.Version, AgentEnvironment.LinuxBoot),
            TestAgents.Status(new ScriptedSignInPrompt { IsAvailable = false }, new AgentLog(_time, TextWriter.Null)),
            _log,
            _time)
        {
            Seeds = new HandedOverSeeds(_seeded.Run.RawDisks, _log, _time),
            Keys = _keys,
            DiskStart = _diskStart,
            Restart = () =>
            {
                _restarts++;
                _callsAtRestart.AddRange(Server.Calls);

                return Task.CompletedTask;
            },
        };

    private Task<BootReturnOutcome> RunAsync() => Return().RunAsync(Server.Stop.Token);

    // The server goes on with the run for the token, and says how often its reports counted a return so far.
    private static AgentRegistrationResult Continued(int? returns = null) =>
        new(StepRunnerFixture.MachineId, MachineState.Deploying, "session-0", "resume-0", 10, null, StepRunnerFixture.RunId, "run-token-2", BootReturns: returns);

    private void AnswerReports() => Server.AnswerRunReports = (_, _) => new AgentRunReportResult("session-1", "resume-1", "run-token-3");

    // What the screen showed, without each line's time and level.
    private string[] Lines() => [.. _console.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Select(line => line[15..])];

    private void AssertNoLineNamesAToken()
    {
        string said = _console + string.Join('\n', Server.SentLines.Select(line => line.Message));

        Assert.DoesNotContain("run-token", said, StringComparison.Ordinal);
        Assert.DoesNotContain("session-", said, StringComparison.Ordinal);
        Assert.DoesNotContain("resume-", said, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADiskThatHoldsNoRunLeavesTheStartAsItWas()
    {
        await _seeded.SeededAsync();
        int calls = Server.Calls.Count;

        BootReturnOutcome outcome = await RunAsync();

        Assert.Equal(BootReturnOutcome.GoesOn, outcome);
        Assert.Equal(calls, Server.Calls.Count);
        Assert.Empty(_keys.Waits);
        Assert.Empty(Lines());
    }

    // A registration without the run's token is what ends a run, so none is made.
    [Fact]
    public async Task ARunTheInstalledLinuxHasIsNotRegisteredAndTheMachineGoesBackToItsDisk()
    {
        await _seeded.HandedOverAsync();
        _seeded.WipeToken();
        byte[] before = TestSeed.Read(_seeded.Disk).Bytes;
        int calls = Server.Calls.Count;

        BootReturnOutcome outcome = await RunAsync();

        Assert.Equal(new BootReturnOutcome(AgentExitCodes.Restarting), outcome);
        Assert.Equal(calls, Server.Calls.Count);
        Assert.Equal([LinuxBootReturn.Wait], _keys.Waits);
        Assert.Equal(TimeSpan.FromSeconds(10), LinuxBootReturn.Wait);
        (int disk, EspPartition? system, bool first) = Assert.Single(_diskStart.Starts);
        Assert.Equal((0, TestRawImage.EspId, false), (disk, system?.PartitionId, first));
        Assert.Equal(1, _restarts);
        Assert.Equal(
            [
                $"Run {StepRunnerFixture.RunId:D} of this machine goes on in the installed Linux on disk 0, which has taken it over. This start came " +
                    "from the network instead of that disk, so the agent does not register and starts the installed Linux again.",
                RestartsUnlessKey,
                "The machine restarts into the installed Linux.",
            ],
            Lines());
        Assert.Equal(before, TestSeed.Read(_seeded.Disk).Bytes);
    }

    [Fact]
    public async Task WithNobodyAtAKeyboardNoKeyIsOffered()
    {
        await _seeded.HandedOverAsync();
        _seeded.WipeToken();
        _keys = new FakeKeyWait { CanPress = false };

        Assert.Equal(AgentExitCodes.Restarting, (await RunAsync()).ExitCode);

        Assert.Contains("The machine restarts into the installed Linux in 10 seconds.", Lines());
        Assert.DoesNotContain(RestartsUnlessKey, Lines());
        Assert.Equal([LinuxBootReturn.Wait], _keys.Waits);
    }

    // The seed must not send a later start from the network back to a run that is over.
    [Fact]
    public async Task AKeyStartsOverOnPurposeWhichEndsTheRunTheInstalledLinuxHas()
    {
        await _seeded.HandedOverAsync();
        _seeded.WipeToken();
        _keys.Pressed = true;
        int calls = Server.Calls.Count;

        BootReturnOutcome outcome = await RunAsync();

        Assert.Equal(BootReturnOutcome.GoesOn with { StartsOver = true }, outcome);
        Assert.Equal(calls, Server.Calls.Count);
        Assert.Empty(_diskStart.Starts);
        Assert.Equal(0, _restarts);
        Assert.Equal(["meta-data", "user-data", "network-config"], TestSeed.Read(_seeded.Disk).Names());
        Assert.Equal(
            [
                "A key was pressed, so the machine starts over here and registers as after any start. That ends the run that is going on " +
                    "in the installed Linux.",
                Removed,
            ],
            Lines()[2..]);
    }

    [Fact]
    public async Task ARunTheInstalledLinuxNeverTookOverIsReportedCountedAndSentRoundOnceMore()
    {
        await _seeded.HandedOverAsync();
        byte[] before = TestSeed.Read(_seeded.Disk).Bytes;
        int calls = Server.Calls.Count;
        List<string> atWait = [];
        List<string> atDiskStart = [];
        _keys.OnWait = () => atWait.AddRange(Server.Calls.Skip(calls));
        _diskStart.OnNext = () => atDiskStart.AddRange(Server.Calls.Skip(calls));
        Server.OnRegister(_ => Continued());
        AnswerReports();

        BootReturnOutcome outcome = await RunAsync();

        // With the token from the seed and no other, from the boot environment.
        AgentRegistration registration = Assert.Single(Server.Registrations);
        Assert.Equal((StepRunnerFixture.RunToken, AgentEnvironment.LinuxBoot), (registration.RunToken, registration.Environment));
        Assert.Null(registration.ResumeToken);
        Assert.Null(registration.StartsOver);

        // The report moves no step, names the installed Linux and carries the count. The lines so far follow it.
        AgentRunReport report = Assert.Single(Server.RunReports);
        Assert.Equal((DeploymentState.Running, SequencePhase.Linux, RunActivity.Restarting, 1), (report.State, report.Phase, report.Activity, report.BootReturns));
        Assert.Empty(report.Steps);
        Assert.Null(report.CurrentStepId);
        Assert.Null(report.Error);
        Assert.Equal(["register", "run-report Running session-0", "log session-1"], atWait);
        Assert.Equal(atWait, atDiskStart);
        Assert.Equal([.. atWait, "log session-1"], _callsAtRestart.Skip(calls));

        // Only then the firmware, with the disk first again, and the restart.
        Assert.Equal(new BootReturnOutcome(AgentExitCodes.Restarting), outcome);
        Assert.Equal([(0, true)], _diskStart.Starts.Select(start => (start.Disk, start.First)));
        Assert.Equal(1, _restarts);
        Assert.Equal(before, TestSeed.Read(_seeded.Disk).Bytes);
        Assert.Contains(
            "The machine started from the network again before the installed Linux took the run over, for the first time. A first start " +
            "can fail or be cut short, so the agent puts the disk first again and restarts the machine into it. The third time, it gives the run up.",
            Lines());
        Assert.Contains(
            $"Run {StepRunnerFixture.RunId:D} of this machine was handed over to the installed Linux on disk 0, and its token is still on that " +
            "disk's cloud-init seed: the installed Linux has not taken the run over. The agent registers with the token.",
            Lines());
        Assert.Equal([RestartsUnlessKey, "The machine restarts into the installed Linux."], Lines()[^2..]);
        Assert.Equal(Lines(), Server.SentLines.Select(line => line.Message));
        AssertNoLineNamesAToken();
    }

    [Fact]
    public async Task TheSecondReturnAddsToWhatTheServerCounted()
    {
        await _seeded.HandedOverAsync();
        Server.OnRegister(_ => Continued(returns: 1));
        AnswerReports();

        Assert.Equal(AgentExitCodes.Restarting, (await RunAsync()).ExitCode);

        Assert.Equal((DeploymentState.Running, 2), (Server.RunReports[^1].State, Server.RunReports[^1].BootReturns));
        Assert.Contains(Lines(), line => line.Contains("for the second time.", StringComparison.Ordinal));
        Assert.Equal(1, _restarts);
    }

    // The token must not stay on the disk of a failed run, and the machine stays the server's, as after any failed run.
    [Fact]
    public async Task AtTheThirdReturnTheRunFailsWithWhatIsKnownAndIsTakenOffTheSeed()
    {
        await _seeded.HandedOverAsync();
        int calls = Server.Calls.Count;
        Server.OnRegister(_ => Continued(returns: 2));
        AnswerReports();

        BootReturnOutcome outcome = await RunAsync();

        AgentRunReport failed = Assert.Single(Server.RunReports);
        Assert.Equal((DeploymentState.Failed, SequencePhase.Linux, NeverTaken, 3), (failed.State, failed.Phase, failed.Error, failed.BootReturns));
        Assert.Empty(failed.Steps);
        Assert.Equal(["register", "log session-0", "run-report Failed session-0"], Server.Calls.Skip(calls));
        Assert.Equal(BootReturnOutcome.GoesOn with { ResumeToken = "resume-1" }, outcome);
        Assert.Empty(_keys.Waits);
        Assert.Empty(_diskStart.Starts);
        Assert.Equal(0, _restarts);

        TestSeed after = TestSeed.Read(_seeded.Disk);
        Assert.Equal(["meta-data", "user-data", "network-config"], after.Names());
        Assert.False(after.Holds(StepRunnerFixture.RunToken));
        Assert.Equal([$"The run failed: {NeverTaken}", Removed], Lines()[^2..]);
        AssertNoLineNamesAToken();
    }

    // Stopped, ended, or another run assigned since. The registration started the machine over, as any does then.
    [Fact]
    public async Task ARunTheServerNoLongerKnowsIsTakenOffTheSeedWithOneSentence()
    {
        await _seeded.HandedOverAsync();
        int calls = Server.Calls.Count;
        Server.OnRegister(_ => new AgentRegistrationResult(StepRunnerFixture.MachineId, MachineState.Pending, "poll-0", "resume-0", 10, null));

        BootReturnOutcome outcome = await RunAsync();

        Assert.Equal(BootReturnOutcome.GoesOn with { ResumeToken = "resume-0" }, outcome);
        Assert.Equal(["register"], Server.Calls.Skip(calls));
        Assert.Empty(_keys.Waits);
        Assert.Equal(0, _restarts);
        TestSeed after = TestSeed.Read(_seeded.Disk);
        Assert.Equal(["meta-data", "user-data", "network-config"], after.Names());
        Assert.False(after.Holds(StepRunnerFixture.RunToken));
        Assert.Equal(
            "The server no longer runs the run that was handed over on disk 0: it was stopped, it ended, or the machine has had another " +
            "since. What the hand-over left on that disk's cloud-init seed is removed, and the agent goes on as after any start.",
            Lines()[^1]);
        Assert.DoesNotContain(Removed, Lines());
        AssertNoLineNamesAToken();
    }

    // An operator stops the run between the registration and the report.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ARunTheServerEndsBeforeItTakesTheReportIsTakenOffTheSeed(bool tokenRefused)
    {
        await _seeded.HandedOverAsync();
        Server.OnRegister(_ => Continued());
        Server.AnswerRunReports = (_, _) => tokenRefused
            ? throw new AgentTokenRejectedException()
            : throw new AgentRequestException("409", "The run is failed and takes no further reports.", HttpStatusCode.Conflict);

        BootReturnOutcome outcome = await RunAsync();

        Assert.Equal(BootReturnOutcome.GoesOn with { ResumeToken = "resume-0" }, outcome);
        Assert.Single(Server.RunReports);
        Assert.Equal(0, _restarts);
        Assert.Equal(["meta-data", "user-data", "network-config"], TestSeed.Read(_seeded.Disk).Names());
        Assert.Contains(Lines(), line => line.StartsWith("The server no longer runs the run that was handed over on disk 0", StringComparison.Ordinal));
    }

    // The sentence must not say that the seed is clear when it is not. The warning before it says why it is not.
    [Fact]
    public async Task ASeedThatCannotBeClearedIsNotSaidToBeClear()
    {
        await _seeded.HandedOverAsync();
        Server.OnRegister(_ => new AgentRegistrationResult(StepRunnerFixture.MachineId, MachineState.Pending, "poll-0", "resume-0", 10, null));
        _seeded.Disk.FailNextWriteAfter = 0;

        BootReturnOutcome outcome = await RunAsync();

        Assert.Equal(BootReturnOutcome.GoesOn with { ResumeToken = "resume-0" }, outcome);
        Assert.Equal(
            [
                "What the hand-over put on the cloud-init seed of disk 0 could not be removed (The device is not ready.). A token there is " +
                    "one the server takes only while it runs that run. A later start from the network finds the seed again.",
                "The server no longer runs the run that was handed over on disk 0: it was stopped, it ended, or the machine has had another " +
                    "since. The agent goes on as after any start.",
            ],
            Lines()[^2..]);
        AssertNoLineNamesAToken();
    }

    // The machine got its boot environment from the server a moment ago, so this is rare. The disk is the better place.
    [Fact]
    public async Task AServerThatCannotBeToldStillHasTheMachineSentBackToItsDisk()
    {
        await _seeded.HandedOverAsync();
        Server.OnRegister(_ => Continued());
        Server.AnswerRunReports = (_, _) => throw new HttpRequestException("The connection was refused.");

        BootReturnOutcome outcome = await RunAsync();

        Assert.Equal(AgentExitCodes.Restarting, outcome.ExitCode);
        Assert.Equal(ServerCallRules.MaxRetries + 1, Server.RunReports.Count);
        Assert.Equal([(0, true)], _diskStart.Starts.Select(start => (start.Disk, start.First)));
        Assert.Equal(1, _restarts);
        Assert.Contains(Lines(), line => line.StartsWith("The server could not be told of this start from the network (", StringComparison.Ordinal));
        Assert.True(TestSeed.Read(_seeded.Disk).Holds(StepRunnerFixture.RunToken));
    }

    [Fact]
    public async Task AKeyAfterTheReportStartsOverAndTakesTheRunOffTheSeed()
    {
        await _seeded.HandedOverAsync();
        _keys.Pressed = true;
        Server.OnRegister(_ => Continued());
        AnswerReports();

        BootReturnOutcome outcome = await RunAsync();

        // No token goes along, so the registration that follows ends the run on the server.
        Assert.Equal(BootReturnOutcome.GoesOn with { StartsOver = true }, outcome);
        Assert.Empty(_diskStart.Starts);
        Assert.Equal(0, _restarts);
        TestSeed after = TestSeed.Read(_seeded.Disk);
        Assert.Equal(["meta-data", "user-data", "network-config"], after.Names());
        Assert.False(after.Holds(StepRunnerFixture.RunToken));
        Assert.Equal(
            [
                "A key was pressed, so the machine starts over here and registers as after any start. That ends the run, which the " +
                    "installed Linux had not taken over.",
                Removed,
            ],
            Lines()[^2..]);
    }

    [Fact]
    public async Task SaysWhichDiskItGoesByWhenTwoHoldARun()
    {
        using SeededRun later = new();
        await _seeded.HandedOverAsync();
        await later.HandedOverAsync(Guid.Parse("0199a4b2-0000-7000-8000-0000000000f2"));
        later.WipeToken();
        _seeded.Run.Tools.Disks.Add(FakeDeploymentTools.Disk(1));
        _seeded.Run.RawDisks.Disks[1] = later.Disk;
        int calls = Server.Calls.Count;

        Assert.Equal(AgentExitCodes.Restarting, (await RunAsync()).ExitCode);

        Assert.Equal(
            "2 disks hold a run that was handed over to the installed Linux: 0, 1. The agent goes by disk 1, whose run was assigned last: " +
            "a machine has one run at a time, so no older one can still go on. The other seeds stay as they are.",
            Lines()[0]);
        Assert.Equal([(1, false)], _diskStart.Starts.Select(start => (start.Disk, start.First)));
        Assert.Equal(calls, Server.Calls.Count);
        Assert.True(TestSeed.Read(_seeded.Disk).Holds(StepRunnerFixture.RunToken));
    }

    [Fact]
    public async Task DisksThatCannotBeListedAreSaidAndTheStartGoesOn()
    {
        await _seeded.HandedOverAsync();

        BootReturnOutcome outcome = await Return(new UnlistedDisks(new IOException("The device is not ready."))).RunAsync(Server.Stop.Token);

        Assert.Equal(BootReturnOutcome.GoesOn, outcome);
        Assert.Equal(
            ["The disks cannot be read (The device is not ready.), so the agent cannot look for a run that was handed over to the installed Linux. It goes on as after any start."],
            Lines());
    }

    [Fact]
    public async Task AStopDuringTheWaitEndsTheAgentWithoutARestart()
    {
        await _seeded.HandedOverAsync();
        _seeded.WipeToken();
        _keys = new FakeKeyWait { Never = true, OnWait = Server.Stop.Cancel };

        BootReturnOutcome outcome = await RunAsync();

        Assert.Equal(new BootReturnOutcome(AgentExitCodes.Stopped), outcome);
        Assert.Empty(_diskStart.Starts);
        Assert.Equal(0, _restarts);
    }

    // The scripted server stops the agent where its answers run out.
    [Fact]
    public async Task AStopWhileItRegistersEndsTheAgentAndLeavesTheSeedAsItIs()
    {
        await _seeded.HandedOverAsync();

        BootReturnOutcome outcome = await RunAsync();

        Assert.Equal(new BootReturnOutcome(AgentExitCodes.Stopped), outcome);
        Assert.Equal(0, _restarts);
        Assert.True(TestSeed.Read(_seeded.Disk).Holds(StepRunnerFixture.RunToken));
        Assert.Equal(LinuxAgentFiles.SeedFolder, TestSeed.Read(_seeded.Disk).Names().Last());
    }
}
