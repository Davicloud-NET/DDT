// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class WindowsFixedDrivesTests
{
    [Fact]
    public void LeavesOutWindowsPEsOwnDrive()
    {
        string? own = Path.GetPathRoot(Environment.SystemDirectory);

        Assert.DoesNotContain(own, WindowsFixedDrives.Roots(), StringComparer.OrdinalIgnoreCase);
    }
}
