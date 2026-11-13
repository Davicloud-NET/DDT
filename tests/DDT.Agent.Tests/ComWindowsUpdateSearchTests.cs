// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.WindowsUpdates;
using Xunit;

namespace DDT.Agent.Tests;

// This PC's real Windows Update, searched only when DDT_TEST_WINDOWS_UPDATE=1: it needs the network and takes seconds.
// It never registers Microsoft Update, downloads or installs.
public sealed class ComWindowsUpdateSearchTests
{
    [Fact]
    public async Task ARealSearchListsUpdatesOrNone()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("DDT_TEST_WINDOWS_UPDATE") != "1", "DDT_TEST_WINDOWS_UPDATE is not 1.");

        using ComWindowsUpdate updates = new();

        IReadOnlyList<AvailableUpdate> found = await updates.SearchAsync(new WindowsUpdateSearch(MicrosoftUpdate: false), TestContext.Current.CancellationToken);

        TestContext.Current.TestOutputHelper?.WriteLine($"{found.Count} updates");

        foreach (AvailableUpdate update in found)
        {
            TestContext.Current.TestOutputHelper?.WriteLine($"{update.Type,-8} {update.Size,14} {string.Join(",", update.Kbs),-10} {update.Title}");
        }

        Assert.All(found, update => Assert.False(string.IsNullOrEmpty(update.Id)));
    }
}
