// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using Xunit;

namespace DDT.Pxe.Tests;

public sealed class BootConnectionsTests
{
    private static readonly IPAddress s_machine = IPAddress.Parse("10.77.0.128");
    private static readonly IPAddress s_other = IPAddress.Parse("10.77.0.129");

    private readonly TestClock _clock = new(DateTimeOffset.FromUnixTimeSeconds(1_791_460_800));
    private readonly BootConnections _connections;

    public BootConnectionsTests() => _connections = new BootConnections(_clock);

    private static IPAddress Address(int index) => new([10, (byte)(index >> 16), (byte)(index >> 8), (byte)index]);

    [Fact]
    public void AMachineThatStartsAgainHasEveryConnectionItLeftOpenReset()
    {
        List<string> reset = [];
        using IDisposable first = _connections.Opened(s_machine, () => reset.Add("first"));
        using IDisposable second = _connections.Opened(s_machine, () => reset.Add("second"));
        using IDisposable other = _connections.Opened(s_other, () => reset.Add("other"));

        Assert.Equal(2, _connections.NetbootStarts(s_machine));

        Assert.Equal(["first", "second"], reset);
        Assert.Equal(1, _connections.Open);
    }

    [Fact]
    public void AConnectionThatEndedByItselfIsNotResetAndLeavesNothingBehind()
    {
        bool reset = false;
        _connections.Opened(s_machine, () => reset = true).Dispose();

        Assert.Equal(0, _connections.Open);
        Assert.Equal(0, _connections.NetbootStarts(s_machine));
        Assert.False(reset);
        Assert.True(_connections.HttpIsSafe(s_machine));
    }

    // Disposing after a reset is what Kestrel does when the reset connection ends, and must not throw or miscount.
    [Fact]
    public void AResetConnectionCanStillBeClosed()
    {
        IDisposable open = _connections.Opened(s_machine, () => { });

        Assert.Equal(1, _connections.NetbootStarts(s_machine));
        open.Dispose();
        open.Dispose();

        Assert.Equal(0, _connections.Open);
    }

    [Fact]
    public void AMachineWithNothingOpenStartsWithoutAMark()
    {
        Assert.Equal(0, _connections.NetbootStarts(s_machine));
        Assert.True(_connections.HttpIsSafe(s_machine));
    }

    // What a reset connection was sending never arrived, so HTTP stays off although no read reported a failure.
    [Fact]
    public void AMachineWhoseConnectionsWereResetIsKeptOffHttp()
    {
        using IDisposable open = _connections.Opened(s_machine, () => { });

        _connections.NetbootStarts(s_machine);

        Assert.False(_connections.HttpIsSafe(s_machine));
        Assert.True(_connections.HttpIsSafe(s_other));
    }

    [Fact]
    public void AReadThatDidNotEndKeepsTheMachineOffHttpForThreeMinutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(3), BootConnections.Remembered);

        _connections.ReadFailed(s_machine);
        Assert.False(_connections.HttpIsSafe(s_machine));

        _clock.Advance(TimeSpan.FromMinutes(3));
        Assert.False(_connections.HttpIsSafe(s_machine));

        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(_connections.HttpIsSafe(s_machine));
    }

    [Fact]
    public void ALaterFailureStartsTheThreeMinutesAgain()
    {
        _connections.ReadFailed(s_machine);
        _clock.Advance(TimeSpan.FromMinutes(2));
        _connections.ReadFailed(s_machine);
        _clock.Advance(TimeSpan.FromMinutes(2));

        Assert.False(_connections.HttpIsSafe(s_machine));
    }

    // Kestrel reports an IPv4 client on a socket for both families in the mapped form.
    [Fact]
    public void TheMappedFormOfAnAddressIsTheSameMachine()
    {
        bool reset = false;
        using IDisposable open = _connections.Opened(s_machine.MapToIPv6(), () => reset = true);

        Assert.Equal(1, _connections.NetbootStarts(s_machine));
        Assert.True(reset);
        Assert.False(_connections.HttpIsSafe(s_machine.MapToIPv6()));
    }

    [Fact]
    public void RemembersNoMoreMachinesThanItsLimitAndForgetsTheOldestFirst()
    {
        for (int index = 0; index < BootConnections.MaxRemembered; index++)
        {
            _connections.ReadFailed(Address(index));
            _clock.Advance(TimeSpan.FromMilliseconds(1));
        }

        IPAddress oldest = Address(0);
        IPAddress second = Address(1);
        Assert.False(_connections.HttpIsSafe(oldest));

        _connections.ReadFailed(s_machine);

        Assert.True(_connections.HttpIsSafe(oldest));
        Assert.False(_connections.HttpIsSafe(second));
        Assert.False(_connections.HttpIsSafe(s_machine));
    }

    [Fact]
    public void AFullListTakesANewMachineByDroppingWhatIsOlderThanThreeMinutes()
    {
        for (int index = 0; index < BootConnections.MaxRemembered; index++)
        {
            _connections.ReadFailed(Address(index));
        }

        _clock.Advance(TimeSpan.FromMinutes(4));
        _connections.ReadFailed(s_machine);
        _connections.ReadFailed(s_other);

        Assert.False(_connections.HttpIsSafe(s_machine));
        Assert.False(_connections.HttpIsSafe(s_other));
    }

    [Fact]
    public async Task TakesConnectionsAndStartsFromManyThreadsAtOnce()
    {
        int reset = 0;

        await Parallel.ForAsync(0, 2000, TestContext.Current.CancellationToken, (index, _) =>
        {
            IPAddress client = Address(index % 16);
            IDisposable open = _connections.Opened(client, () => Interlocked.Increment(ref reset));

            if (index % 3 == 0)
            {
                _connections.NetbootStarts(client);
            }

            open.Dispose();

            return ValueTask.CompletedTask;
        });

        Assert.Equal(0, _connections.Open);
        Assert.True(reset > 0);
    }
}
