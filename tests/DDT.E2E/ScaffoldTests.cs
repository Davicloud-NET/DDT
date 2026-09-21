// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Xunit;

namespace DDT.E2E;

public sealed class ScaffoldTests
{
    [Fact]
    [Trait("Category", "E2E")]
    public void ProjectIsWiredIntoTheTestRun()
    {
        Assert.True(true);
    }
}
