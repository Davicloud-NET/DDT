// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.WindowsUpdates;
using Xunit;

namespace DDT.Agent.Tests;

// What an IUpdate's fields become for the step's filter.
public sealed class UpdateFieldsTests
{
    private static readonly UpdateFields s_cumulative = new("6f1c6e3a-0000-4000-8000-000000000001", 201, "2026-11 Cumulative Update (KB5070001)", ["5070001"])
    {
        Type = 1,
        CategoryIds = ["0fa1201d-4330-4fa8-8ae9-b877473b6441", "e6cf1350-c01b-414d-a61f-263d14d133b4"],
        EulaAccepted = true,
        MaxDownloadSize = 97_216_300_205m,
    };

    [Fact]
    public void ASoftwareUpdateKeepsItsIdentityTitleAndKbs()
    {
        AvailableUpdate update = s_cumulative.ToAvailableUpdate();

        Assert.Equal(("6f1c6e3a-0000-4000-8000-000000000001", 201, "2026-11 Cumulative Update (KB5070001)"), (update.Id, update.Revision, update.Title));
        Assert.Equal(["5070001"], update.Kbs);
        Assert.Equal(UpdateType.Software, update.Type);
        Assert.True(update.EulaAccepted);
        Assert.Equal(97_216_300_205L, update.Size);
    }

    [Fact]
    public void CategoryIdsBecomeGuidsAndAnUnreadableOneIsDropped()
    {
        AvailableUpdate update = (s_cumulative with { CategoryIds = ["3689BDC8-B205-4AF4-8D4A-A63924C5E9D5", ""] }).ToAvailableUpdate();

        Assert.Equal([UpdateKinds.Upgrades], update.CategoryIds);
        Assert.True(UpdateKinds.IsFeatureUpgrade(update));
    }

    [Fact]
    public void TypeTwoIsADriverThatMayAskForInputWithoutWantingIt()
    {
        AvailableUpdate update = (s_cumulative with { Type = 2, CanRequestUserInput = true }).ToAvailableUpdate();

        Assert.Equal(UpdateType.Driver, update.Type);
        Assert.True(UpdateKinds.IsDriver(update));
        Assert.False(UpdateKinds.WantsInput(update));
    }

    [Fact]
    public void BrowseOnlyAndBetaMakeAnUpdateOptional()
    {
        Assert.True(UpdateKinds.IsOptional((s_cumulative with { BrowseOnly = true }).ToAvailableUpdate()));
        Assert.True(UpdateKinds.IsOptional((s_cumulative with { IsBeta = true }).ToAvailableUpdate()));
        Assert.False(UpdateKinds.IsOptional(s_cumulative.ToAvailableUpdate()));
    }

    [Fact]
    public void AnUnacceptedEulaAndASoftwareUpdateThatWantsInputCarryOver()
    {
        AvailableUpdate update = (s_cumulative with { EulaAccepted = false, CanRequestUserInput = true }).ToAvailableUpdate();

        Assert.False(update.EulaAccepted);
        Assert.True(UpdateKinds.WantsInput(update));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(154_000_000, 154_000_000)]
    public void TheSizeIsTheMaximumDownloadInBytes(long size, long expected)
    {
        Assert.Equal(expected, (s_cumulative with { MaxDownloadSize = size }).ToAvailableUpdate().Size);
    }

    [Fact]
    public void ASizeBeyondALongIsCapped()
    {
        Assert.Equal(long.MaxValue, (s_cumulative with { MaxDownloadSize = decimal.MaxValue }).ToAvailableUpdate().Size);
    }
}
