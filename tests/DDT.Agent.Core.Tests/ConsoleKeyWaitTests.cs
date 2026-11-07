// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Xunit;

namespace DDT.Agent.Tests;

// The wait for a key at the text console, which stops the countdown before a restart into the installed Linux.
public sealed class ConsoleKeyWaitTests
{
    private static readonly TimeSpan s_time = TimeSpan.FromSeconds(10);

    private readonly ImmediateTimeProvider _time = new();

    // Someone leaned on the keyboard while the machine started. That must decide nothing.
    [Fact]
    public async Task KeysFromBeforeTheWaitDoNotCountAndAreTakenOffTheConsole()
    {
        FakeConsoleKeys keys = new() { Waiting = 3 };

        bool pressed = await new ConsoleKeyWait(_time, keys).PressedWithinAsync(s_time, TestContext.Current.CancellationToken);

        Assert.False(pressed);
        Assert.Equal((3, 0), (keys.Taken, keys.Waiting));
        Assert.Equal(200, _time.Delays.Count);
        Assert.All(_time.Delays, delay => Assert.Equal(TimeSpan.FromMilliseconds(50), delay));
    }

    // A held key arrives many times. None of them may land in the question that follows.
    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    public async Task AKeyDuringTheWaitEndsItAndLeavesNoneOnTheConsole(int arriving)
    {
        FakeConsoleKeys keys = new() { ArriveAtPoll = 40, Arriving = arriving };

        bool pressed = await new ConsoleKeyWait(_time, keys).PressedWithinAsync(s_time, TestContext.Current.CancellationToken);

        Assert.True(pressed);
        Assert.Equal((arriving, 0), (keys.Taken, keys.Waiting));
        Assert.InRange(_time.Delays.Count, 1, 199);
    }

    [Fact]
    public async Task WithRedirectedInputNobodyCanPressAndTheWaitRunsItsTime()
    {
        FakeConsoleKeys keys = new() { IsRedirected = true, Waiting = 1 };
        ConsoleKeyWait wait = new(_time, keys);

        Assert.False(wait.CanPress);
        Assert.False(await wait.PressedWithinAsync(s_time, TestContext.Current.CancellationToken));

        Assert.Equal([s_time], _time.Delays);
        Assert.Equal((0, 0), (keys.Polls, keys.Taken));
    }

    [Fact]
    public async Task AStopEndsTheWait()
    {
        using CancellationTokenSource stop = new();
        await stop.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ConsoleKeyWait(_time, new FakeConsoleKeys()).PressedWithinAsync(s_time, stop.Token));
    }
}
