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

// What the Linux boot environment does before it registers when a disk holds a run that was handed over and whose token
// is still on the seed: it registers with that token, reports the start, and sends the machine round or gives the run up.
public sealed class LinuxBootReturnTests : IDisposable
{
    private const string NeverTaken =
        "The machine started from the network 3 times after the run was handed over, and each time the run's token was still on " +
        "the cloud-init seed, so the installed Linux never took the run over. The image has no cloud-init or no systemd, the " +
        "user-data keeps cloud-init from running DDT's script, the machine does not start from its disk, or that script failed. " +
        "Look at the machine's own screen while it starts from its disk, and in the installed Linux at /var/log/cloud-init-output.log.";

    private const string RestartsUnlessKey = BootReturnStart.RestartsUnlessKey;

    private const string Removed = BootReturnStart.Removed;

    private readonly BootReturnStart _start = new();

    public void Dispose() => _start.Dispose();

    private ScriptedAgentServer Server => _start.Server;

    private Task<BootReturnOutcome> RunAsync() => _start.RunAsync();

    // The server goes on with the run for the token, and says how often its reports counted a return so far.
    private static AgentRegistrationResult Continued(int? returns = null) =>
        new(StepRunnerFixture.MachineId, MachineState.Deploying, "session-0", "resume-0", 10, null, StepRunnerFixture.RunId, "run-token-2", BootReturns: returns);

    private void AnswerReports() => Server.AnswerRunReports = (_, _) => new AgentRunReportResult("session-1", "resume-1", "run-token-3");

    private string[] Lines() => _start.Lines();

    private void AssertNoLineNamesAToken() => _start.AssertNoLineNamesAToken();

    [Fact]
    public async Task ADiskThatHoldsNoRunLeavesTheStartAsItWas()
    {
        await _start.Seeded.SeededAsync();
        int calls = Server.Calls.Count;

        BootReturnOutcome outcome = await RunAsync();

        Assert.Equal(BootReturnOutcome.GoesOn, outcome);
        Assert.Equal(calls, Server.Calls.Count);
        Assert.Empty(_start.Keys.Waits);
        Assert.Empty(Lines());
    }

    [Fact]
    public async Task ARunTheInstalledLinuxNeverTookOverIsReportedCountedAndSentRoundOnceMore()
    {
        await _start.Seeded.HandedOverAsync();
        byte[] before = TestSeed.Read(_start.Seeded.Disk).Bytes;
        int calls = Server.Calls.Count;
        List<string> atWait = [];
        List<string> atDiskStart = [];
        _start.Keys.OnWait = () => atWait.AddRange(Server.Calls.Skip(calls));
        _start.DiskStart.OnNext = () => atDiskStart.AddRange(Server.Calls.Skip(calls));
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
        Assert.Equal([.. atWait, "log session-1"], _start.CallsAtRestart.Skip(calls));

        // Only then the firmware, with the disk first again, and the restart.
        Assert.Equal(new BootReturnOutcome(AgentExitCodes.Restarting), outcome);
        Assert.Equal([(0, true)], _start.DiskStart.Starts.Select(start => (start.Disk, start.First)));
        Assert.Equal(1, _start.Restarts);
        Assert.Equal(before, TestSeed.Read(_start.Seeded.Disk).Bytes);
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
        await _start.Seeded.HandedOverAsync();
        Server.OnRegister(_ => Continued(returns: 1));
        AnswerReports();

        Assert.Equal(AgentExitCodes.Restarting, (await RunAsync()).ExitCode);

        Assert.Equal((DeploymentState.Running, 2), (Server.RunReports[^1].State, Server.RunReports[^1].BootReturns));
        Assert.Contains(Lines(), line => line.Contains("for the second time.", StringComparison.Ordinal));
        Assert.Equal(1, _start.Restarts);
    }

    // The token must not stay on the disk of a failed run, and the machine stays the server's, as after any failed run.
    [Fact]
    public async Task AtTheThirdReturnTheRunFailsWithWhatIsKnownAndIsTakenOffTheSeed()
    {
        await _start.Seeded.HandedOverAsync();
        int calls = Server.Calls.Count;
        Server.OnRegister(_ => Continued(returns: 2));
        AnswerReports();

        BootReturnOutcome outcome = await RunAsync();

        AgentRunReport failed = Assert.Single(Server.RunReports);
        Assert.Equal((DeploymentState.Failed, SequencePhase.Linux, NeverTaken, 3), (failed.State, failed.Phase, failed.Error, failed.BootReturns));
        Assert.Empty(failed.Steps);
        Assert.Equal(["register", "log session-0", "run-report Failed session-0"], Server.Calls.Skip(calls));
        Assert.Equal(BootReturnOutcome.GoesOn with { ResumeToken = "resume-1" }, outcome);
        Assert.Empty(_start.Keys.Waits);
        Assert.Empty(_start.DiskStart.Starts);
        Assert.Equal(0, _start.Restarts);

        TestSeed after = TestSeed.Read(_start.Seeded.Disk);
        Assert.Equal(["meta-data", "user-data", "network-config"], after.Names());
        Assert.False(after.Holds(StepRunnerFixture.RunToken));
        Assert.Equal([$"The run failed: {NeverTaken}", Removed], Lines()[^2..]);
        AssertNoLineNamesAToken();
    }

    // Stopped, ended, or another run assigned since. The registration started the machine over, as any does then.
    [Fact]
    public async Task ARunTheServerNoLongerKnowsIsTakenOffTheSeedWithOneSentence()
    {
        await _start.Seeded.HandedOverAsync();
        int calls = Server.Calls.Count;
        Server.OnRegister(_ => new AgentRegistrationResult(StepRunnerFixture.MachineId, MachineState.Pending, "poll-0", "resume-0", 10, null));

        BootReturnOutcome outcome = await RunAsync();

        Assert.Equal(BootReturnOutcome.GoesOn with { ResumeToken = "resume-0" }, outcome);
        Assert.Equal(["register"], Server.Calls.Skip(calls));
        Assert.Empty(_start.Keys.Waits);
        Assert.Equal(0, _start.Restarts);
        TestSeed after = TestSeed.Read(_start.Seeded.Disk);
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
        await _start.Seeded.HandedOverAsync();
        Server.OnRegister(_ => Continued());
        Server.AnswerRunReports = (_, _) => tokenRefused
            ? throw new AgentTokenRejectedException()
            : throw new AgentRequestException("409", "The run is failed and takes no further reports.", HttpStatusCode.Conflict);

        BootReturnOutcome outcome = await RunAsync();

        Assert.Equal(BootReturnOutcome.GoesOn with { ResumeToken = "resume-0" }, outcome);
        Assert.Single(Server.RunReports);
        Assert.Equal(0, _start.Restarts);
        Assert.Equal(["meta-data", "user-data", "network-config"], TestSeed.Read(_start.Seeded.Disk).Names());
        Assert.Contains(Lines(), line => line.StartsWith("The server no longer runs the run that was handed over on disk 0", StringComparison.Ordinal));
    }

    // The sentence must not say that the seed is clear when it is not. The warning before it says why it is not.
    [Fact]
    public async Task ASeedThatCannotBeClearedIsNotSaidToBeClear()
    {
        await _start.Seeded.HandedOverAsync();
        Server.OnRegister(_ => new AgentRegistrationResult(StepRunnerFixture.MachineId, MachineState.Pending, "poll-0", "resume-0", 10, null));
        _start.Seeded.Disk.FailNextWriteAfter = 0;

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
        await _start.Seeded.HandedOverAsync();
        Server.OnRegister(_ => Continued());
        Server.AnswerRunReports = (_, _) => throw new HttpRequestException("The connection was refused.");

        BootReturnOutcome outcome = await RunAsync();

        Assert.Equal(AgentExitCodes.Restarting, outcome.ExitCode);
        Assert.Equal(ServerCallRules.MaxRetries + 1, Server.RunReports.Count);
        Assert.Equal([(0, true)], _start.DiskStart.Starts.Select(start => (start.Disk, start.First)));
        Assert.Equal(1, _start.Restarts);
        Assert.Contains(Lines(), line => line.StartsWith("The server could not be told of this start from the network (", StringComparison.Ordinal));
        Assert.True(TestSeed.Read(_start.Seeded.Disk).Holds(StepRunnerFixture.RunToken));
    }

    // The run's token never goes along with the word that someone started over: a valid one would keep the run.
    [Fact]
    public async Task AKeyAndTheTypedWordAfterTheReportStartOverAndTakeTheRunOffTheSeed()
    {
        await _start.Seeded.HandedOverAsync();
        _start.Keys.Pressed = true;
        _start.Prompt = new ScriptedSignInPrompt("END");
        Server.OnRegister(_ => Continued());
        Server.OnRegister(_ => BootReturnStart.StartedOver("resume-9"));
        AnswerReports();

        BootReturnOutcome outcome = await RunAsync();

        Assert.Equal(BootReturnOutcome.GoesOn with { ResumeToken = "resume-9" }, outcome);
        Assert.Equal([null, true], Server.Registrations.Select(registration => registration.StartsOver));
        Assert.Equal((null, null), (Server.Registrations[1].RunToken, Server.Registrations[1].ResumeToken));
        Assert.Empty(_start.DiskStart.Starts);
        Assert.Equal(0, _start.Restarts);
        TestSeed after = TestSeed.Read(_start.Seeded.Disk);
        Assert.Equal(["meta-data", "user-data", "network-config"], after.Names());
        Assert.False(after.Holds(StepRunnerFixture.RunToken));
        Assert.Equal(
            [
                "A key was pressed, so the restart waits. Starting over here ends the run, which the installed Linux has not taken over: " +
                    "the machine registers as after any start and has to be authorized again. Type END to do that. Any other answer, or " +
                    "none within 60 seconds, restarts the machine into the installed Linux.",
                "END was typed, so the machine starts over here: the agent registers without the run's token and tells the server that " +
                    "someone at the machine chose to. That ends the run, which the installed Linux had not taken over.",
                Removed,
            ],
            Lines()[^3..]);
    }

    [Fact]
    public async Task AKeyWithoutTheTypedWordLeavesTheRunAndTheMachineGoesRoundOnceMore()
    {
        await _start.Seeded.HandedOverAsync();
        _start.Keys.Pressed = true;
        Server.OnRegister(_ => Continued());
        AnswerReports();

        BootReturnOutcome outcome = await RunAsync();

        Assert.Equal(AgentExitCodes.Restarting, outcome.ExitCode);
        Assert.Single(Server.Registrations);
        Assert.Equal([(0, true)], _start.DiskStart.Starts.Select(start => (start.Disk, start.First)));
        Assert.True(TestSeed.Read(_start.Seeded.Disk).Holds(StepRunnerFixture.RunToken));
    }

    // Someone signed in under Windows PE and picked a Linux sequence, and the seed still holds an old run's dead token.
    // Without the carried token the look's registration would start the machine over, and the loop's once more.
    [Fact]
    public async Task AResumeTokenTheFirmwareCarriesGoesAlongWithTheSeedsTokenSoADeadOneStartsNothingOver()
    {
        await _start.Seeded.HandedOverAsync();
        _start.Carried = "resume-carried";
        Server.OnRegister(_ => new AgentRegistrationResult(StepRunnerFixture.MachineId, MachineState.Approved, "session-0", "resume-1", 10, "ada"));

        BootReturnOutcome outcome = await RunAsync();

        AgentRegistration registration = Assert.Single(Server.Registrations);
        Assert.Equal(("resume-carried", StepRunnerFixture.RunToken), (registration.ResumeToken, registration.RunToken));
        Assert.Null(registration.HandedOver);
        Assert.Equal(1, _start.CarriedTaken);
        Assert.Equal(BootReturnOutcome.GoesOn with { ResumeToken = "resume-1" }, outcome);
        Assert.Equal(["meta-data", "user-data", "network-config"], TestSeed.Read(_start.Seeded.Disk).Names());
        Assert.StartsWith("The server no longer runs the run that was handed over on disk 0", Lines()[^1], StringComparison.Ordinal);
        AssertNoLineNamesAToken();
    }

    // The loop takes it then, as after any start.
    [Fact]
    public async Task WithoutARunOnAnyDiskTheCarriedTokenStaysInTheFirmwareForTheLoop()
    {
        await _start.Seeded.SeededAsync();
        _start.Carried = "resume-carried";

        Assert.Equal(BootReturnOutcome.GoesOn, await RunAsync());

        Assert.Equal(0, _start.CarriedTaken);
    }

    // The look has a lister of its own, so the disks are not said twice at every start.
    [Fact]
    public async Task DisksThatCannotBeListedAreSaidAndTheStartGoesOn()
    {
        await _start.Seeded.HandedOverAsync();

        BootReturnOutcome outcome = await _start.Return(new UnlistedDisks(new IOException("The device is not ready."))).RunAsync(Server.Stop.Token);

        Assert.Equal(BootReturnOutcome.GoesOn, outcome);
        Assert.Equal(
            ["The disks cannot be read (The device is not ready.), so the agent cannot look for a run that was handed over to the installed Linux. It goes on as after any start."],
            Lines());
    }

    // The scripted server stops the agent where its answers run out.
    [Fact]
    public async Task AStopWhileItRegistersEndsTheAgentAndLeavesTheSeedAsItIs()
    {
        await _start.Seeded.HandedOverAsync();

        BootReturnOutcome outcome = await RunAsync();

        Assert.Equal(new BootReturnOutcome(AgentExitCodes.Stopped), outcome);
        Assert.Equal(0, _start.Restarts);
        Assert.True(TestSeed.Read(_start.Seeded.Disk).Holds(StepRunnerFixture.RunToken));
        Assert.Equal(LinuxAgentFiles.SeedFolder, TestSeed.Read(_start.Seeded.Disk).Names().Last());
    }
}
