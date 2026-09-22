// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Sequences;
using DDT.Agent.WindowsPhase;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class WindowsServiceHostTests
{
    // The service control manager did not start the test process, so the dispatcher refuses at once and runs nothing.
    [Fact]
    public void RefusesToRunOutsideTheServiceControlManager()
    {
        bool ran = false;

        bool started = WindowsServiceHost.TryRun(
            _ =>
            {
                ran = true;

                return Task.FromResult(0);
            },
            out int error);

        Assert.False(started);
        Assert.False(ran);
        Assert.Equal(1063, error);
    }

    [Fact]
    public void TheArgumentIsTheOneTheHandOverRegisters()
    {
        Assert.EndsWith($" {WindowsServiceHost.Argument}", OfflineServiceRegistration.ImagePath, StringComparison.Ordinal);
    }
}
