// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.NetworkInformation;

namespace DDT.Pxe.Tests;

// A row of the system's connection table, as a test wants it.
internal sealed class FakeTcpConnection(string remote, int localPort, TcpState state = TcpState.TimeWait) : TcpConnectionInformation
{
    public override IPEndPoint LocalEndPoint { get; } = new(IPAddress.Parse("10.77.0.1"), localPort);

    public override IPEndPoint RemoteEndPoint { get; } = new(IPAddress.Parse(remote), 21550);

    public override TcpState State { get; } = state;
}
