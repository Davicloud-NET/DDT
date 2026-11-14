// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.WindowsUpdates;
using Xunit;

namespace DDT.Agent.Tests;

// The one thread every Windows Update call runs on.
public sealed class ComThreadTests
{
    [Fact]
    public async Task EveryItemRunsOnTheSameMtaThreadThatIsNotTheCallers()
    {
        using ComThread thread = new("Test", () => { });

        (int Id, ApartmentState Apartment, bool Pooled) first = await thread.RunAsync(Where);
        (int Id, ApartmentState Apartment, bool Pooled) second = await thread.RunAsync(Where);

        Assert.Equal(first, second);
        Assert.NotEqual(Environment.CurrentManagedThreadId, first.Id);
        Assert.Equal(ApartmentState.MTA, first.Apartment);
        Assert.False(first.Pooled);
    }

    [Fact]
    public async Task ItemsRunInTheOrderTheyCame()
    {
        using ComThread thread = new("Test", () => { });
        List<int> order = [];

        await Task.WhenAll(Enumerable.Range(0, 20).Select(index => thread.RunAsync(() =>
        {
            order.Add(index);

            return index;
        })));

        Assert.Equal(Enumerable.Range(0, 20), order);
    }

    [Fact]
    public async Task AFailureReachesTheCaller()
    {
        using ComThread thread = new("Test", () => { });

        ComCallException failure = await Assert.ThrowsAsync<ComCallException>(() => thread.RunAsync<int>(() => throw new ComCallException("BeginSearch", unchecked((int)0x80240032), null)));

        Assert.Equal("BeginSearch (0x80240032, invalid search criteria)", failure.Message);
        Assert.Equal(2, await thread.RunAsync(() => 2));
    }

    [Fact]
    public async Task DisposeRunsTheEndingOnTheThreadAndEndsIt()
    {
        int? endedOn = null;
        ComThread thread = new("Test", () => endedOn = Environment.CurrentManagedThreadId);
        int ranOn = await thread.RunAsync(() => Environment.CurrentManagedThreadId);

        thread.Dispose();

        Assert.Equal(ranOn, endedOn);
        Assert.False(thread.IsAlive);
        Assert.True(thread.Disposing.IsCancellationRequested);
    }

    [Fact]
    public void NothingRunsAfterDispose()
    {
        ComThread thread = new("Test", () => { });
        thread.Dispose();
        thread.Dispose();

        Assert.Throws<ObjectDisposedException>(() => { _ = thread.RunAsync(() => 1); });
    }

    // A running item sees Disposing and ends; one still queued behind it never starts.
    [Fact]
    public async Task DisposeStopsARunningItemAndDropsTheQueuedOnes()
    {
        ComThread thread = new("Test", () => { });
        using ManualResetEventSlim started = new();
        Task<bool> running = thread.RunAsync(() =>
        {
            started.Set();

            return thread.Disposing.WaitHandle.WaitOne(TimeSpan.FromMinutes(1));
        });
        Task<int> queued = thread.RunAsync(() => 1);
        started.Wait(TestContext.Current.CancellationToken);

        thread.Dispose();

        Assert.True(await running);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => queued);
        Assert.False(thread.IsAlive);
    }

    [Fact]
    public async Task ComWindowsUpdateEndsItsThreadOnDisposeWithoutHavingCalledWindowsUpdate()
    {
        ComWindowsUpdate updates = new();

        updates.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => updates.IsBusyAsync(TestContext.Current.CancellationToken));
    }

    private static (int Id, ApartmentState Apartment, bool Pooled) Where() =>
        (Environment.CurrentManagedThreadId, Thread.CurrentThread.GetApartmentState(), Thread.CurrentThread.IsThreadPoolThread);
}
