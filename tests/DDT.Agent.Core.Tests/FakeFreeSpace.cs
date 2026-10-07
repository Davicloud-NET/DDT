// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;

namespace DDT.Agent.Tests;

// Every volume has as much room as the test says, and all a run could ask for unless it says so.
internal sealed class FakeFreeSpace : IFreeSpace
{
    public long Available { get; set; } = long.MaxValue;

    public long AvailableBytes(string path) => Available;
}
