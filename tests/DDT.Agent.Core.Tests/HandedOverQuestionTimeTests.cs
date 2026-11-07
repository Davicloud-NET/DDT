// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Core.CloudInit;
using Xunit;

namespace DDT.Agent.Tests;

// The Linux boot agent looks at the disks before its update check. Where the question about a run there has half a
// minute, the check gets it too, and the question has what the check left of it.
public sealed class HandedOverQuestionTimeTests : IDisposable
{
    private readonly BootReturnStart _start = new();

    private readonly ManualTimeProvider _time = new();

    public void Dispose() => _start.Dispose();

    private SeededRun Seeded => _start.Seeded;

    private ScriptedAgentServer Server => _start.Server;

    private Task<TimeSpan?> LookAsync(HandedOverQuestionTime time, IDisks? disks = null) =>
        time.ForUpdateCheckAsync(
            disks ?? Seeded.Run.Tools,
            new HandedOverSeeds(Seeded.Run.RawDisks, new AgentLog(_time, TextWriter.Null), _time),
            TestContext.Current.CancellationToken);

    // The seed as the installed Linux leaves it once it has the run: the state without the token.
    private async Task TakenAsync()
    {
        await Seeded.HandedOverAsync();
        Seeded.WipeToken();
    }

    [Fact]
    public async Task ARunTheInstalledLinuxTookOverGivesTheCheckTheQuestionsHalfMinuteWhichRunsFromTheLook()
    {
        await TakenAsync();
        HandedOverQuestionTime time = new(_time);

        Assert.Equal(TimeSpan.FromSeconds(30), await LookAsync(time));

        _time.Advance(TimeSpan.FromSeconds(12));
        Assert.Equal(TimeSpan.FromSeconds(18), time.Left);
    }

    public static TheoryData<string> SeedsWithoutALimit => ["no seed", "a token", "a state that names no run", "disks that cannot be listed"];

    // Each of these waits for the server as after any start, so the check does too.
    [Theory]
    [MemberData(nameof(SeedsWithoutALimit))]
    public async Task WhereTheQuestionHasNoLimitTheCheckHasNoneAndTheTimeDoesNotRun(string disk)
    {
        if (disk != "no seed")
        {
            await Seeded.HandedOverAsync();
        }

        if (disk == "a state that names no run")
        {
            Seeded.WipeToken();
            Seeded.ChangeSeed(file => SeedVolume.SamePath(file.Path, LinuxAgentFiles.SeedState) ? file with { Content = [] } : file);
        }

        HandedOverQuestionTime time = new(_time);
        IDisks? disks = disk == "disks that cannot be listed" ? new UnlistedDisks(new IOException("The device is not ready.")) : null;

        Assert.Null(await LookAsync(time, disks));

        _time.Advance(TimeSpan.FromSeconds(40));
        Assert.Equal(TimeSpan.FromSeconds(30), time.Left);
    }

    // The proof's server was stopped as the boot agent started. The check used the half minute up, and the machine
    // waited for the server instead of going back to its disk.
    [Fact]
    public async Task AfterACheckThatWaitedTheWholeHalfMinuteTheQuestionIsNotAskedAndTheMachineGoesBackToItsDisk()
    {
        await TakenAsync();
        HandedOverQuestionTime time = new(_time);
        await LookAsync(time);
        _time.Advance(TimeSpan.FromSeconds(30));
        DateTimeOffset asked = _time.GetUtcNow();

        BootReturnOutcome outcome = await _time.ThroughWaitsAsync(_start.Return(time: _time, questionTime: time).RunAsync(Server.Stop.Token));

        // Only the two seconds the last lines stay on the screen.
        Assert.Empty(Server.Registrations);
        Assert.InRange(_time.GetUtcNow() - asked, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3));
        Assert.Equal(AgentExitCodes.Restarting, outcome.ExitCode);
        Assert.Equal([(0, false)], _start.DiskStart.Starts.Select(start => (start.Disk, start.First)));
        Assert.Contains(LinuxBootReturnTexts.NotAsked, _start.Lines());
    }

    [Fact]
    public async Task TheQuestionHasWhatTheCheckLeftOfTheHalfMinute()
    {
        await TakenAsync();
        HandedOverQuestionTime time = new(_time);
        await LookAsync(time);
        _time.Advance(TimeSpan.FromSeconds(12));
        DateTimeOffset asked = _time.GetUtcNow();

        for (int attempt = 0; attempt < 12; attempt++)
        {
            Server.OnRegister(_ => throw new HttpRequestException("The connection was refused."));
        }

        BootReturnOutcome outcome = await _time.ThroughWaitsAsync(_start.Return(time: _time, questionTime: time).RunAsync(Server.Stop.Token));

        // Tries after 0, 2, 6 and 14 of the 18 seconds left, and the two seconds of the last lines.
        Assert.Equal(4, Server.Registrations.Count);
        Assert.InRange(_time.GetUtcNow() - asked, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(21));
        Assert.Equal(AgentExitCodes.Restarting, outcome.ExitCode);
        Assert.Contains(LinuxBootReturnTexts.NotAsked, _start.Lines());
    }

    [Fact]
    public async Task AServerThatIsThereAnswersTheQuestionAsBefore()
    {
        await TakenAsync();
        HandedOverQuestionTime time = new(_time);
        await LookAsync(time);
        _time.Advance(TimeSpan.FromSeconds(1));
        Server.OnRegister(_ => BootReturnStart.GoesOn());

        BootReturnOutcome outcome = await _time.ThroughWaitsAsync(_start.Return(time: _time, questionTime: time).RunAsync(Server.Stop.Token));

        Assert.Equal(StepRunnerFixture.RunId, Assert.Single(Server.Registrations).HandedOver?.RunId);
        Assert.Equal(AgentExitCodes.Restarting, outcome.ExitCode);
        Assert.DoesNotContain(LinuxBootReturnTexts.NotAsked, _start.Lines());
    }
}
