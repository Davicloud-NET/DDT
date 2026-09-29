// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Live;
using Xunit;

namespace DDT.Server.Tests;

public sealed class PushThrottleTests
{
    private readonly ManualTimeProvider _clock = new();
    private readonly List<string> _sent = [];

    private Func<Task> Push(string payload) => () =>
    {
        lock (_sent)
        {
            _sent.Add(payload);
        }

        return Task.CompletedTask;
    };

    private string[] Sent()
    {
        lock (_sent)
        {
            return [.. _sent];
        }
    }

    // The trailing push runs in the timer's continuation.
    // Advancing the clock only starts it, so this waits until the push is sent.
    private async Task<string[]> SentAfterAsync(int count)
    {
        for (int attempt = 0; attempt < 500 && Sent().Length < count; attempt++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        return Sent();
    }

    [Fact]
    public async Task SendsTheFirstAtOnceAndOnlyTheLatestOfTheRestWhenTheSecondEnds()
    {
        PushThrottle throttle = new(_clock, TimeSpan.FromSeconds(1), CancellationToken.None);
        Guid machine = Guid.NewGuid();

        throttle.Push(machine, Push("first"));
        throttle.Push(machine, Push("second"));
        throttle.Push(machine, Push("third"));
        throttle.Push(Guid.NewGuid(), Push("another machine"));

        Assert.Equal(["first", "another machine"], Sent());

        _clock.Advance(TimeSpan.FromMilliseconds(999));
        Assert.Equal(2, Sent().Length);

        _clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(["first", "another machine", "third"], await SentAfterAsync(3));

        // The trailing push started a new one-second window.
        throttle.Push(machine, Push("fourth"));
        Assert.Equal(3, Sent().Length);

        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal("fourth", (await SentAfterAsync(4))[^1]);

        _clock.Advance(TimeSpan.FromSeconds(5));
        throttle.Push(machine, Push("fifth"));
        Assert.Equal("fifth", Sent()[^1]);
    }

    // When the server stops, a waiting push is dropped instead of keeping its timer.
    [Fact]
    public async Task AStopDropsThePushThatWaits()
    {
        using CancellationTokenSource stopping = new();
        PushThrottle throttle = new(_clock, TimeSpan.FromSeconds(1), stopping.Token);
        Guid machine = Guid.NewGuid();

        throttle.Push(machine, Push("first"));
        throttle.Push(machine, Push("second"));
        Assert.True(_clock.HasTimerDueIn(TimeSpan.FromSeconds(1)));

        await stopping.CancelAsync();

        Assert.False(_clock.HasTimerDueIn(TimeSpan.FromSeconds(1)));
        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(["first"], Sent());
    }
}
