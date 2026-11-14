// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.WindowsUpdates;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class UpdateNamesTests
{
    [Theory]
    [InlineData(unchecked((int)0x8024000B), "0x8024000B, cancelled")]
    [InlineData(unchecked((int)0x80240032), "0x80240032, invalid search criteria")]
    [InlineData(0x00240003, "0x00240003, succeeded with errors")]
    [InlineData(unchecked((int)0x80070005), "0x80070005")]
    public void AnHResultReadsAsHexAndTheCodesTheSpikeSawHaveNames(int hResult, string text)
    {
        Assert.Equal(text, UpdateNames.HResult(hResult));
    }

    [Fact]
    public void AnUpdateIsNamedByItsTitleAndTheKbNumbersTheTitleLacks()
    {
        AvailableUpdate update = new("a", 1, "Defender platform update", ["4052623"]);

        Assert.Equal("Defender platform update (KB4052623)", UpdateNames.Of(update));
    }
}
