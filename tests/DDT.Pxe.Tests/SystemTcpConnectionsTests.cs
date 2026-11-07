// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Xunit;

namespace DDT.Pxe.Tests;

public sealed class SystemTcpConnectionsTests
{
    private static readonly IPAddress s_machine = IPAddress.Parse("10.77.0.128");

    private readonly TestClock _clock = new(DateTimeOffset.FromUnixTimeSeconds(1_791_460_800));
    private readonly List<TcpConnectionInformation> _rows = [];
    private int _readings;

    private SystemTcpConnections Table() => new(_clock, () =>
    {
        _readings++;

        return [.. _rows];
    });

    [Theory]
    [InlineData(TcpState.Established)]
    [InlineData(TcpState.FinWait1)]
    [InlineData(TcpState.FinWait2)]
    [InlineData(TcpState.TimeWait)]
    [InlineData(TcpState.LastAck)]
    public void AConnectionCountsInWhateverStateTheSystemHoldsIt(TcpState state)
    {
        _rows.Add(new FakeTcpConnection("10.77.0.128", 8080, state));

        Assert.True(Table().Any(s_machine, 8080));
    }

    [Fact]
    public void OnlyTheMachinesOwnConnectionsWithThePortCount()
    {
        _rows.Add(new FakeTcpConnection("10.77.0.129", 8080));
        _rows.Add(new FakeTcpConnection("10.77.0.128", 8443));
        SystemTcpConnections table = Table();

        Assert.False(table.Any(s_machine, 8080));
        Assert.True(table.Any(IPAddress.Parse("10.77.0.129"), 8080));
        Assert.True(table.Any(s_machine, 8443));
    }

    // A socket for both families lists an IPv4 machine in the mapped form.
    [Fact]
    public void TheMappedFormOfAnAddressIsTheSameMachine()
    {
        _rows.Add(new FakeTcpConnection("::ffff:10.77.0.128", 8080));
        _rows.Add(new FakeTcpConnection("10.77.0.130", 8080));
        SystemTcpConnections table = Table();

        Assert.True(table.Any(s_machine, 8080));
        Assert.True(table.Any(IPAddress.Parse("10.77.0.130").MapToIPv6(), 8080));
    }

    [Fact]
    public void ReadsTheSystemsListOnceASecondHoweverOftenItIsAsked()
    {
        Assert.Equal(TimeSpan.FromSeconds(1), SystemTcpConnections.Kept);
        SystemTcpConnections table = Table();

        Assert.False(table.Any(s_machine, 8080));
        _rows.Add(new FakeTcpConnection("10.77.0.128", 8080));
        _clock.Advance(TimeSpan.FromMilliseconds(999));
        Assert.False(table.Any(s_machine, 8080));
        Assert.Equal(1, _readings);

        _clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(table.Any(s_machine, 8080));
        Assert.Equal(2, _readings);
    }

    // A clock that was set back must not keep an old reading for hours.
    [Fact]
    public void ReadsAgainAfterTheClockWentBack()
    {
        SystemTcpConnections table = Table();
        Assert.False(table.Any(s_machine, 8080));
        _rows.Add(new FakeTcpConnection("10.77.0.128", 8080));

        _clock.Advance(TimeSpan.FromHours(-2));

        Assert.True(table.Any(s_machine, 8080));
    }

    [Fact]
    public void ASystemThatDoesNotListItsConnectionsHoldsNoneAndIsAskedAgainLater()
    {
        bool fails = true;
        SystemTcpConnections table = new(_clock, () => fails
            ? throw new PlatformNotSupportedException()
            : [new FakeTcpConnection("10.77.0.128", 8080)]);

        Assert.False(table.Any(s_machine, 8080));

        fails = false;
        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(table.Any(s_machine, 8080));
    }

    [Fact]
    public void AListTheSystemCannotReadHoldsNone()
    {
        SystemTcpConnections table = new(_clock, () => throw new NetworkInformationException());

        Assert.False(table.Any(s_machine, 8080));
    }

    // The real list of this computer. The server closes first, as the boot port does after each answer, and its system
    // keeps the connection in TIME_WAIT after both sides are closed, where no program can reach it any more.
    [Fact]
    public async Task TheSystemStillHoldsAConnectionTheServerClosedFirstAfterBothSidesAreClosed()
    {
        using Socket listener = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Listen();
        int port = ((IPEndPoint)listener.LocalEndPoint!).Port;
        SystemTcpConnections table = new(TimeProvider.System);
        Assert.False(table.Any(IPAddress.Loopback, port));

        Socket machine = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await machine.ConnectAsync(new IPEndPoint(IPAddress.Loopback, port), TestContext.Current.CancellationToken);
        Socket server = await listener.AcceptAsync(TestContext.Current.CancellationToken);
        Assert.True(new SystemTcpConnections(TimeProvider.System).Any(IPAddress.Loopback, port));

        server.Shutdown(SocketShutdown.Send);
        Assert.Equal(0, await machine.ReceiveAsync(new byte[1], SocketFlags.None, TestContext.Current.CancellationToken));
        machine.Dispose();
        Assert.Equal(0, await server.ReceiveAsync(new byte[1], SocketFlags.None, TestContext.Current.CancellationToken));
        server.Dispose();

        Assert.True(new SystemTcpConnections(TimeProvider.System).Any(IPAddress.Loopback, port));
    }
}
