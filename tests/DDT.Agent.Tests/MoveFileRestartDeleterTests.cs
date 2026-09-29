// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.WindowsPhase;
using Xunit;

namespace DDT.Agent.Tests;

// Calling MoveFileEx for real would mark a path on this computer, so the test only checks what it's asked to do.
public sealed class MoveFileRestartDeleterTests
{
    // With any other flag and no new name, MoveFileEx deletes nothing when Windows next starts, and C:\DDT stays.
    [Fact]
    public void AsksForTheDeletionAtTheNextStartOfWindows()
    {
        Assert.Equal(4u, MoveFileRestartDeleter.DelayUntilReboot);
    }
}
