// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Consoles;
using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Contracts.Agents;
using DDT.Contracts.Machines;
using DDT.Core.CloudInit;
using Xunit;

namespace DDT.Agent.Tests;

// The look finds a run's state on the seed and no token. The installed Linux may have the run, or the run is long over,
// so the agent asks the server before it sends the machine back to its disk, and only starts over on a typed word.
public sealed class LinuxBootReturnAskTests : IDisposable
{
    private const string GoesOn =
        $"The server says that run 0193a4b2-0000-7000-8000-0000000000f1 of this machine goes on in the installed Linux, and has changed nothing. " +
        "This is start 1 of 5 from the network in a row without a call from the installed Linux in between. At start 5, the server ends " +
        "the run. The agent starts disk 0 again.";

    private const string NoLongerRuns =
        "The server no longer runs the run that was handed over on disk 0: it was stopped, it ended, or the machine has had another " +
        "since. What the hand-over left on that disk's cloud-init seed is removed, and the agent goes on as after any start.";

    private const string Asked =
        "A key was pressed, so the restart waits. Starting over here ends the run that goes on in the installed Linux: the machine " +
        "registers as after any start and has to be authorized again. Type END to do that. Any other answer, or none within 60 seconds, " +
        "restarts the machine into the installed Linux.";

    private readonly BootReturnStart _start = new();

    public void Dispose() => _start.Dispose();

    private ScriptedAgentServer Server => _start.Server;

    private SeededRun Seeded => _start.Seeded;

    // The seed as the installed Linux leaves it once it has the run: the state without the token.
    private async Task TakenAsync()
    {
        await Seeded.HandedOverAsync();
        Seeded.WipeToken();
    }

    [Fact]
    public async Task AsksTheServerAndGoesBackToTheDiskWhileTheRunGoesOnInTheInstalledLinux()
    {
        Assert.Equal(StepRunnerFixture.RunId, Guid.Parse("0193a4b2-0000-7000-8000-0000000000f1"));
        await TakenAsync();
        byte[] before = TestSeed.Read(Seeded.Disk).Bytes;
        int calls = Server.Calls.Count;
        Server.OnRegister(_ => BootReturnStart.GoesOn());

        BootReturnOutcome outcome = await _start.RunAsync();

        // The question carries no token and names the run and this start. The server's answer hands no token out, and
        // nothing else is said to it.
        AgentRegistration question = Assert.Single(Server.Registrations);
        Assert.Equal(((Guid?)StepRunnerFixture.RunId, AgentEnvironment.LinuxBoot), (question.HandedOver?.RunId, question.Environment));
        Assert.NotNull(question.HandedOver?.Start);
        Assert.Null(question.RunToken);
        Assert.Null(question.ResumeToken);
        Assert.Null(question.StartsOver);
        Assert.Equal(["register"], Server.Calls.Skip(calls));

        Assert.Equal(new BootReturnOutcome(AgentExitCodes.Restarting), outcome);
        Assert.Equal([LinuxBootReturn.Wait], _start.Keys.Waits);
        Assert.Equal(TimeSpan.FromSeconds(10), LinuxBootReturn.Wait);
        (int disk, EspPartition? system, bool first) = Assert.Single(_start.DiskStart.Starts);
        Assert.Equal((0, TestRawImage.EspId, false), (disk, system?.PartitionId, first));
        Assert.Equal(1, _start.Restarts);
        // The question is a registration, so the machine's two lines of facts follow it on the screen.
        Assert.Equal(
            $"Run {StepRunnerFixture.RunId:D} of this machine was handed over to the installed Linux on disk 0, and that disk's cloud-init seed " +
            "holds its state without its token, as it does once the installed Linux has taken the run over. This start came from the " +
            "network instead of that disk, so the agent asks the server whether the run still goes on there.",
            _start.Lines()[0]);
        Assert.Equal([GoesOn, BootReturnStart.RestartsUnlessKey, BootReturnStart.RestartsNow], _start.Lines()[3..]);
        Assert.Equal(before, TestSeed.Read(Seeded.Disk).Bytes);
        Assert.Empty(_start.Prompt.Labels);
    }

    // The server counts starts, so a start names itself. An agent that starts again is another start.
    [Fact]
    public async Task EachStartOfTheAgentNamesItselfWithAnIdOfItsOwn()
    {
        await TakenAsync();
        Server.OnRegister(_ => BootReturnStart.GoesOn()).OnRegister(_ => BootReturnStart.GoesOn(2));

        await _start.RunAsync();
        await _start.RunAsync();

        Guid?[] starts = [.. Server.Registrations.Select(registration => registration.HandedOver?.Start)];
        Assert.Equal(2, starts.Length);
        Assert.All(starts, start => Assert.NotNull(start));
        Assert.NotEqual(starts[0], starts[1]);
    }

    // The proof's screen held the last two lines for a third of a second.
    [Fact]
    public async Task TheLastLinesStayOnTheScreenForTwoSecondsBeforeTheRestart()
    {
        await TakenAsync();
        Server.OnRegister(_ => BootReturnStart.GoesOn());

        await _start.RunAsync();

        Assert.Equal(TimeSpan.FromSeconds(2), LinuxBootReturn.Shown);
        Assert.Equal(LinuxBootReturn.Shown, _start.Time.Delays[^1]);
    }

    // Stopped on the web, ended, or another run since: the machine is the operator's again, as before this look existed.
    [Fact]
    public async Task ARunThatIsOverOnTheServerIsTakenOffTheSeedAndTheStartGoesOn()
    {
        await TakenAsync();
        Server.OnRegister(_ => BootReturnStart.StartedOver());

        BootReturnOutcome outcome = await _start.RunAsync();

        Assert.Equal(BootReturnOutcome.GoesOn with { ResumeToken = "resume-0" }, outcome);
        Assert.Single(Server.Registrations);
        Assert.Empty(_start.Keys.Waits);
        Assert.Empty(_start.DiskStart.Starts);
        Assert.Equal(0, _start.Restarts);
        Assert.Equal(["meta-data", "user-data", "network-config"], TestSeed.Read(Seeded.Disk).Names());
        Assert.Equal(NoLongerRuns, _start.Lines()[^1]);
        _start.AssertNoLineNamesAToken();
    }

    // The machine got its boot environment from the server a moment ago, so this is rare. The disk is the better place.
    [Fact]
    public async Task AServerThatCannotBeAskedWithinHalfAMinuteStillHasTheMachineSentBackToItsDisk()
    {
        await TakenAsync();
        ManualTimeProvider time = new();
        DateTimeOffset began = time.GetUtcNow();

        for (int attempt = 0; attempt < 12; attempt++)
        {
            Server.OnRegister(_ => throw new HttpRequestException("The connection was refused."));
        }

        BootReturnOutcome outcome = await time.ThroughWaitsAsync(_start.Return(time: time).RunAsync(Server.Stop.Token));

        // After 0, 2, 6 and 14 seconds. A try at 30 seconds could not be answered within them, so the agent makes none
        // and the wait before it ends there. The last lines then stay for their two seconds.
        Assert.Equal(TimeSpan.FromSeconds(30), LinuxBootReturn.AskTime);
        Assert.Equal(4, Server.Registrations.Count);
        Assert.NotNull(Server.Registrations[0].HandedOver?.Start);
        Assert.Equal(StepRunnerFixture.RunId, Server.Registrations[0].HandedOver?.RunId);
        Assert.All(Server.Registrations, registration => Assert.Equal(Server.Registrations[0].HandedOver, registration.HandedOver));
        Assert.InRange(time.GetUtcNow() - began, TimeSpan.FromSeconds(32), TimeSpan.FromSeconds(33));
        Assert.Equal(AgentExitCodes.Restarting, outcome.ExitCode);
        Assert.Equal([(0, false)], _start.DiskStart.Starts.Select(start => (start.Disk, start.First)));
        Assert.Equal(1, _start.Restarts);
        Assert.Contains(
            "The server did not answer within 30 seconds, so the agent could not ask it whether the run still goes on in the installed " +
            "Linux. It goes by what the disk says.",
            _start.Lines());
        Assert.Equal([BootReturnStart.RestartsUnlessKey, BootReturnStart.RestartsNow], _start.Lines()[^2..]);
    }

    // The half minute ends a try too. A server that takes the call and says nothing would otherwise double it.
    [Fact]
    public async Task AServerThatTakesTheQuestionAndSaysNothingIsGivenUpAfterTheSameHalfMinute()
    {
        await TakenAsync();
        ManualTimeProvider time = new();
        DateTimeOffset began = time.GetUtcNow();
        Server.LeavesRegistrationsUnanswered = true;

        BootReturnOutcome outcome = await time.ThroughWaitsAsync(_start.Return(time: time).RunAsync(Server.Stop.Token));

        Assert.Single(Server.Registrations);
        Assert.InRange(time.GetUtcNow() - began, TimeSpan.FromSeconds(32), TimeSpan.FromSeconds(33));
        Assert.Equal(AgentExitCodes.Restarting, outcome.ExitCode);
        Assert.Equal([(0, false)], _start.DiskStart.Starts.Select(start => (start.Disk, start.First)));
        Assert.Contains(LinuxBootReturnTexts.NotAsked, _start.Lines());
        Assert.DoesNotContain(_start.Lines(), line => line.StartsWith("Cannot register with the server", StringComparison.Ordinal));
    }

    public static TheoryData<string> StatesThatNameNoRun => ["", "{ not json", "\0\0\0\0\0\0\0\0", "a folder"];

    // Such a state is no proof that the installed Linux has a run, and none that it has not. The server knows.
    [Theory]
    [MemberData(nameof(StatesThatNameNoRun))]
    public async Task AStateThatNamesNoRunAsksWithoutANameAndGoesBackOnlyWhenTheServerSaysARunGoesOn(string state)
    {
        await TakenAsync();
        Seeded.ChangeSeed(file => !SeedVolume.SamePath(file.Path, LinuxAgentFiles.SeedState) ? file
            : state == "a folder" ? new SeedFile($"{LinuxAgentFiles.SeedState}/inner", [1])
            : file with { Content = System.Text.Encoding.ASCII.GetBytes(state) });
        Server.OnRegister(_ => BootReturnStart.GoesOn());

        BootReturnOutcome outcome = await _start.RunAsync();

        AgentHandedOverRun asked = Assert.IsType<AgentHandedOverRun>(Assert.Single(Server.Registrations).HandedOver);
        Assert.Null(asked.RunId);
        Assert.NotNull(asked.Start);
        Assert.Equal(AgentExitCodes.Restarting, outcome.ExitCode);
        Assert.Equal([(0, false)], _start.DiskStart.Starts.Select(start => (start.Disk, start.First)));
        Assert.Equal(
            "The cloud-init seed of disk 0 holds what a hand-over to the installed Linux left, with no token and no state that names a " +
            "run. That alone says nothing of a run that goes on, so the agent asks the server whether one of this machine goes on in " +
            "the installed Linux.",
            _start.Lines()[0]);
        Assert.Equal(GoesOn, _start.Lines()[^3]);
    }

    // What the hand-over left is then only a leftover, and must not send every later start from the network round.
    [Fact]
    public async Task AStateThatNamesNoRunIsALeftoverOnceTheServerSaysNoRunGoesOn()
    {
        await TakenAsync();
        Seeded.ChangeSeed(file => SeedVolume.SamePath(file.Path, LinuxAgentFiles.SeedState) ? file with { Content = [] } : file);
        Server.OnRegister(_ => BootReturnStart.StartedOver());

        BootReturnOutcome outcome = await _start.RunAsync();

        Assert.Equal(BootReturnOutcome.GoesOn with { ResumeToken = "resume-0" }, outcome);
        Assert.Equal(["meta-data", "user-data", "network-config"], TestSeed.Read(Seeded.Disk).Names());
        Assert.Equal(0, _start.Restarts);
        Assert.Equal(NoLongerRuns, _start.Lines()[^1]);
    }

    // Without a name the seed gives the agent nothing to go by alone, so it waits for the server as after any start.
    [Fact]
    public async Task AQuestionWithoutANameIsAskedUntilTheServerAnswers()
    {
        await TakenAsync();
        Seeded.ChangeSeed(file => SeedVolume.SamePath(file.Path, LinuxAgentFiles.SeedState) ? file with { Content = [] } : file);
        ManualTimeProvider time = new();
        DateTimeOffset began = time.GetUtcNow();

        for (int attempt = 0; attempt < 8; attempt++)
        {
            Server.OnRegister(_ => throw new HttpRequestException("The connection was refused."));
        }

        Server.OnRegister(_ => BootReturnStart.StartedOver());

        BootReturnOutcome outcome = await time.ThroughWaitsAsync(_start.Return(time: time).RunAsync(Server.Stop.Token));

        // Two and a half minutes of the usual waits between tries, five times the half minute a question with a name gets.
        Assert.Equal(BootReturnOutcome.GoesOn with { ResumeToken = "resume-0" }, outcome);
        Assert.Equal(9, Server.Registrations.Count);
        Assert.InRange(time.GetUtcNow() - began, TimeSpan.FromSeconds(150), TimeSpan.FromSeconds(151));
    }

    [Fact]
    public async Task WithNobodyAtAKeyboardNoKeyIsOffered()
    {
        await TakenAsync();
        _start.Keys = new FakeKeyWait { CanPress = false };
        Server.OnRegister(_ => BootReturnStart.GoesOn());

        Assert.Equal(AgentExitCodes.Restarting, (await _start.RunAsync()).ExitCode);

        Assert.Contains(BootReturnStart.RestartsSoon, _start.Lines());
        Assert.DoesNotContain(BootReturnStart.RestartsUnlessKey, _start.Lines());
        Assert.Equal([LinuxBootReturn.Wait], _start.Keys.Waits);
    }

    public static TheoryData<string?, string> AnswersThatEndNothing => new()
    {
        { null, "Nobody answered within 60 seconds, so the run goes on and the machine restarts into the installed Linux." },
        { "", StartOverQuestion.NotConfirmed },
        { "end", StartOverQuestion.NotConfirmed },
        { "yes", StartOverQuestion.NotConfirmed },
        { "ENDE", StartOverQuestion.NotConfirmed },
    };

    // One key within ten seconds once ended a run for good. A held key, or a cat on the keyboard, must not.
    [Theory]
    [MemberData(nameof(AnswersThatEndNothing))]
    public async Task AKeyOnlyStopsTheCountdownAndAnythingButTheWordGoesBackToTheDisk(string? typed, string said)
    {
        await TakenAsync();
        byte[] before = TestSeed.Read(Seeded.Disk).Bytes;
        _start.Keys.Pressed = true;
        _start.Prompt = typed is null ? new ScriptedSignInPrompt() : new ScriptedSignInPrompt(typed);
        Server.OnRegister(_ => BootReturnStart.GoesOn());

        BootReturnOutcome outcome = await _start.RunAsync();

        Assert.Equal(new BootReturnOutcome(AgentExitCodes.Restarting), outcome);
        Assert.Single(Server.Registrations);
        Assert.Equal(["Type END to end the run and start over here"], _start.Prompt.Labels);
        Assert.Equal([(0, false)], _start.DiskStart.Starts.Select(start => (start.Disk, start.First)));
        Assert.Equal(1, _start.Restarts);
        Assert.Equal(before, TestSeed.Read(Seeded.Disk).Bytes);
        Assert.Equal([BootReturnStart.RestartsUnlessKey, Asked, said, BootReturnStart.RestartsNow], _start.Lines()[^4..]);
    }

    // The seed goes first, so a later start from the network cannot find the run again. Then one registration says it.
    [Fact]
    public async Task TheTypedWordStartsOverWithOneRegistrationThatSaysSo()
    {
        await TakenAsync();
        _start.Keys.Pressed = true;
        _start.Prompt = new ScriptedSignInPrompt(" END ");
        bool seedClearedFirst = false;
        Server.OnRegister(_ => BootReturnStart.GoesOn());
        Server.OnRegister(_ =>
        {
            seedClearedFirst = TestSeed.Read(Seeded.Disk).Names().SequenceEqual(["meta-data", "user-data", "network-config"]);

            return BootReturnStart.StartedOver("resume-9");
        });

        BootReturnOutcome outcome = await _start.RunAsync();

        // No token goes along, which is what ends the run on the server. The loop goes on from this registration's.
        Assert.Equal(BootReturnOutcome.GoesOn with { ResumeToken = "resume-9" }, outcome);
        Assert.Equal([null, true], Server.Registrations.Select(registration => registration.StartsOver));
        AgentRegistration startsOver = Server.Registrations[1];
        Assert.Null(startsOver.RunToken);
        Assert.Null(startsOver.ResumeToken);
        Assert.Null(startsOver.HandedOver);
        Assert.True(seedClearedFirst);
        Assert.Empty(_start.DiskStart.Starts);
        Assert.Equal(0, _start.Restarts);
        Assert.Equal(
            [
                Asked,
                "END was typed, so the machine starts over here: the agent registers without the run's token and tells the server that " +
                    "someone at the machine chose to. That ends the run that is going on in the installed Linux.",
                BootReturnStart.Removed,
            ],
            _start.Lines()[^3..]);
        _start.AssertNoLineNamesAToken();
    }

    [Fact]
    public async Task SaysWhichDiskItGoesByWhenTwoHoldARun()
    {
        using SeededRun later = new();
        await Seeded.HandedOverAsync();
        Guid laterRun = Guid.Parse("0199a4b2-0000-7000-8000-0000000000f2");
        await later.HandedOverAsync(laterRun);
        later.WipeToken();
        Seeded.Run.Tools.Disks.Add(FakeDeploymentTools.Disk(1));
        Seeded.Run.RawDisks.Disks[1] = later.Disk;
        Server.OnRegister(_ => BootReturnStart.GoesOn());

        Assert.Equal(AgentExitCodes.Restarting, (await _start.RunAsync()).ExitCode);

        Assert.Equal(
            "2 disks hold a run that was handed over to the installed Linux: 0, 1. The agent goes by disk 1, whose run was assigned last: " +
            "a machine has one run at a time, so no older one can still go on. The other seeds stay as they are.",
            _start.Lines()[0]);
        Assert.Equal(laterRun, Assert.Single(Server.Registrations).HandedOver?.RunId);
        Assert.Equal([(1, false)], _start.DiskStart.Starts.Select(start => (start.Disk, start.First)));
        Assert.True(TestSeed.Read(Seeded.Disk).Holds(StepRunnerFixture.RunToken));
    }

    // The newer seed's state cannot be read, so the older run's name must not be the one the agent asks about.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WithASeedThatNamesNoRunBesideAnotherTheQuestionNamesNoneAndTheAnswerSaysWhichDisk(bool answerNamesTheOtherSeedsRun)
    {
        using SeededRun unnamed = new();
        await TakenAsync();
        await unnamed.HandedOverAsync();
        unnamed.ChangeSeed(file => SeedVolume.SamePath(file.Path, LinuxAgentFiles.SeedState) ? file with { Content = [] }
            : SeedVolume.SamePath(file.Path, LinuxAgentFiles.SeedToken) ? null
            : file);
        Seeded.Run.Tools.Disks.Add(FakeDeploymentTools.Disk(1));
        Seeded.Run.RawDisks.Disks[1] = unnamed.Disk;
        Guid goesOn = answerNamesTheOtherSeedsRun ? StepRunnerFixture.RunId : Guid.Parse("0199a4b2-0000-7000-8000-0000000000f3");
        Server.OnRegister(_ => BootReturnStart.GoesOn() with { InstalledRun = new AgentInstalledRun(goesOn, 1, 5) });

        Assert.Equal(AgentExitCodes.Restarting, (await _start.RunAsync()).ExitCode);

        Assert.Equal(
            "2 disks hold a run that was handed over to the installed Linux: 0, 1. The seed of disk 1 has no token and names no run the " +
            "agent can read, so the agent cannot tell which run was assigned last. It asks the server, and goes by the disk whose seed " +
            "names the run the server says goes on, or else by disk 1. The other seeds stay as they are.",
            _start.Lines()[0]);
        AgentHandedOverRun asked = Assert.IsType<AgentHandedOverRun>(Assert.Single(Server.Registrations).HandedOver);
        Assert.Null(asked.RunId);
        Assert.Equal([(answerNamesTheOtherSeedsRun ? 0 : 1, false)], _start.DiskStart.Starts.Select(start => (start.Disk, start.First)));
        Assert.EndsWith($"The agent starts disk {(answerNamesTheOtherSeedsRun ? 0 : 1)} again.", _start.Lines()[^3], StringComparison.Ordinal);
    }

    // A file system that cannot be read is no proof that the installed Linux has no run. The server knows.
    [Fact]
    public async Task ASeedThatCannotBeReadAsksWithoutANameAndGoesBackToTheDiskWhileARunGoesOn()
    {
        await TakenAsync();
        Seeded.DamageSeedEntry("DDT~1");
        byte[] before = TestSeed.Read(Seeded.Disk).Bytes;
        Server.OnRegister(_ => BootReturnStart.GoesOn());

        BootReturnOutcome outcome = await _start.RunAsync();

        AgentHandedOverRun asked = Assert.IsType<AgentHandedOverRun>(Assert.Single(Server.Registrations).HandedOver);
        Assert.Null(asked.RunId);
        Assert.Null(Server.Registrations[0].RunToken);
        Assert.Equal(AgentExitCodes.Restarting, outcome.ExitCode);
        Assert.Equal([(0, false)], _start.DiskStart.Starts.Select(start => (start.Disk, start.First)));
        Assert.Equal(
            [
                HandedOverSeeds.SeedUnreadMessage(FakeDeploymentTools.Disk(0), "A cluster chain on the partition's FAT file system is damaged."),
                "What the cloud-init seed of disk 0 holds is not known. A registration would end a run that goes on in the installed Linux, " +
                    "so the agent first asks the server whether one of this machine goes on there.",
            ],
            _start.Lines()[..2]);
        Assert.Equal(GoesOn, _start.Lines()[^3]);
        Assert.Equal(before, TestSeed.Read(Seeded.Disk).Bytes);
    }

    [Fact]
    public async Task ASeedThatCannotBeReadStaysAsItIsOnceTheServerSaysNoRunGoesOn()
    {
        await TakenAsync();
        Seeded.DamageSeedEntry("DDT~1");
        byte[] before = TestSeed.Read(Seeded.Disk).Bytes;
        Server.OnRegister(_ => BootReturnStart.StartedOver());

        BootReturnOutcome outcome = await _start.RunAsync();

        Assert.Equal(BootReturnOutcome.GoesOn with { ResumeToken = "resume-0" }, outcome);
        Assert.Equal(0, _start.Restarts);
        Assert.Equal(before, TestSeed.Read(Seeded.Disk).Bytes);
        Assert.Equal(
            "The server says that no run of this machine goes on in the installed Linux. The cloud-init seed of disk 0, which the agent " +
            "cannot read, stays as it is, and the agent goes on as after any start.",
            _start.Lines()[^1]);
    }

    // A later change of boot environment from Windows PE comes with a resume token, which keeps the machine signed in.
    [Fact]
    public async Task AResumeTokenTheFirmwareCarriesGoesAlongWithTheQuestionSoTheMachineIsNotStartedOver()
    {
        await TakenAsync();
        _start.Carried = "resume-carried";
        Server.OnRegister(_ => new AgentRegistrationResult(StepRunnerFixture.MachineId, MachineState.Approved, "session-0", "resume-1", 10, "ada"));

        BootReturnOutcome outcome = await _start.RunAsync();

        AgentRegistration question = Assert.Single(Server.Registrations);
        Assert.Equal(("resume-carried", (Guid?)StepRunnerFixture.RunId), (question.ResumeToken, question.HandedOver?.RunId));
        Assert.Equal(1, _start.CarriedTaken);
        Assert.Equal(BootReturnOutcome.GoesOn with { ResumeToken = "resume-1" }, outcome);
        Assert.Equal(NoLongerRuns, _start.Lines()[^1]);
        _start.AssertNoLineNamesAToken();
    }

    // The server went on with the run for the resume token. The seed holds no token, so the installed Linux has it.
    [Fact]
    public async Task ARunTheServerGoesOnWithForACarriedTokenStillSendsTheMachineBackToItsDisk()
    {
        await TakenAsync();
        _start.Carried = "resume-carried";
        Server.OnRegister(_ => new AgentRegistrationResult(StepRunnerFixture.MachineId, MachineState.Deploying, "session-0", "resume-1", 10, null, StepRunnerFixture.RunId, "run-token-2"));

        BootReturnOutcome outcome = await _start.RunAsync();

        Assert.Equal(AgentExitCodes.Restarting, outcome.ExitCode);
        Assert.Equal([(0, false)], _start.DiskStart.Starts.Select(start => (start.Disk, start.First)));
        Assert.Single(Server.Registrations);
        _start.AssertNoLineNamesAToken();
    }

    [Fact]
    public async Task AStopDuringTheWaitEndsTheAgentWithoutARestart()
    {
        await TakenAsync();
        _start.Keys = new FakeKeyWait { Never = true, OnWait = Server.Stop.Cancel };
        Server.OnRegister(_ => BootReturnStart.GoesOn());

        BootReturnOutcome outcome = await _start.RunAsync();

        Assert.Equal(new BootReturnOutcome(AgentExitCodes.Stopped), outcome);
        Assert.Empty(_start.DiskStart.Starts);
        Assert.Equal(0, _start.Restarts);
    }

    // The scripted server stops the agent where its answers run out.
    [Fact]
    public async Task AStopWhileItAsksEndsTheAgentAndLeavesTheSeedAsItIs()
    {
        await TakenAsync();
        byte[] before = TestSeed.Read(Seeded.Disk).Bytes;

        BootReturnOutcome outcome = await _start.RunAsync();

        Assert.Equal(new BootReturnOutcome(AgentExitCodes.Stopped), outcome);
        Assert.Equal(0, _start.Restarts);
        Assert.Equal(before, TestSeed.Read(Seeded.Disk).Bytes);
        Assert.DoesNotContain(_start.Lines(), line => line.StartsWith("The server did not answer", StringComparison.Ordinal));
    }
}
