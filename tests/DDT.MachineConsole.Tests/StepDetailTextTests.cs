// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.ConsoleProtocol;
using DDT.MachineConsole.Texts;
using Xunit;

namespace DDT.MachineConsole.Tests;

// A coded step detail comes in the console's language; any other detail as the agent wrote it.
public sealed class StepDetailTextTests
{
    [Theory]
    [InlineData(UiLanguage.English, "#downloadingUpdates 5", "Downloading 5 updates")]
    [InlineData(UiLanguage.English, "#installingUpdates 1", "Installing 1 update")]
    [InlineData(UiLanguage.German, "#downloadingUpdates 1", "1 Update wird heruntergeladen")]
    [InlineData(UiLanguage.German, "#installingUpdates 5", "5 Updates werden installiert")]
    [InlineData(UiLanguage.German, "7-Zip 24.08", "7-Zip 24.08")]
    [InlineData(UiLanguage.German, "#somethingNew 2", "#somethingNew 2")]
    [InlineData(UiLanguage.German, "#installingUpdates", "#installingUpdates")]
    public void SaysTheKnownDetailsInTheConsolesLanguage(UiLanguage language, string detail, string expected)
    {
        Assert.Equal(expected, Say.StepDetail(Localizer.Embedded(language), detail));
    }

    [Fact]
    public void ReadsBackWhatTheAgentCodes()
    {
        Assert.True(StepDetails.TryRead(StepDetails.Coded(StepDetails.InstallingUpdates, 12), out string key, out int? number));
        Assert.Equal((StepDetails.InstallingUpdates, 12), (key, number));
        Assert.False(StepDetails.TryRead("# 5", out _, out _));
        Assert.False(StepDetails.TryRead("#key -5", out _, out _));
        Assert.False(StepDetails.TryRead(null, out _, out _));
    }
}
