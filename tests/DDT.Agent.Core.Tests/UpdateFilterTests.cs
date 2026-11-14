// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.WindowsUpdates;
using DDT.Contracts.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

// What a Windows Update step installs, by category id and type, never by the localised names.
public sealed class UpdateFilterTests
{
    private static readonly AvailableUpdate s_cumulative = new("cu", 1, "2026-11 Cumulative Update (KB5070001)", ["5070001"]);
    private static readonly AvailableUpdate s_upgrade = new("up", 1, "Windows 11, version 26H2", []) { CategoryIds = [UpdateKinds.Upgrades] };
    private static readonly AvailableUpdate s_driver = new("dr", 1, "Intel - Display - 31.0.101.5590", []) { Type = UpdateType.Driver, CanRequestUserInput = true };
    private static readonly AvailableUpdate s_driverByCategory = new("dc", 1, "Realtek - Net", []) { CategoryIds = [UpdateKinds.Drivers] };
    private static readonly AvailableUpdate s_preview = new("pr", 1, "2026-11 Cumulative Update Preview", ["5070009"]) { BrowseOnly = true };
    private static readonly AvailableUpdate s_beta = new("be", 1, "Beta", ["5070010"]) { IsBeta = true };
    private static readonly AvailableUpdate s_asks = new("as", 1, "Asks", ["5070011"]) { CanRequestUserInput = true };
    private static readonly AvailableUpdate s_excluded = new("ex", 1, "Excluded", ["5070012", "5070013"]);

    private static readonly AvailableUpdate[] s_all = [s_cumulative, s_upgrade, s_driver, s_driverByCategory, s_preview, s_beta, s_asks, s_excluded];

    private static WindowsUpdateStep Step(bool drivers = false, bool optional = false) =>
        new() { Id = Guid.NewGuid(), Name = "Windows Update", Drivers = drivers, OptionalUpdates = optional, ExcludedKbs = ["kb5070013"] };

    [Fact]
    public void ByDefaultInstallsNeitherUpgradesNorDriversNorOptionalOnesNorWhatAsksOrIsLeftOut()
    {
        UpdateSelection selection = UpdateFilter.Choose(s_all, Step(), new UpdateCheckpoint());

        Assert.Equal([s_cumulative], selection.ToInstall);
        Assert.Equal(
            [
                ("up", UpdateFilter.FeatureUpgrade),
                ("dr", UpdateFilter.Driver),
                ("dc", UpdateFilter.Driver),
                ("pr", UpdateFilter.Optional),
                ("be", UpdateFilter.Optional),
                ("as", UpdateFilter.WantsInput),
                ("ex", UpdateFilter.Excluded),
            ],
            selection.LeftOut.Select(left => (left.Update.Id, left.Reason)));
    }

    // Every driver says it may ask for input, so that is ignored for drivers. A feature upgrade is never installed.
    [Fact]
    public void TheSwitchesBringDriversAndOptionalUpdates()
    {
        UpdateSelection selection = UpdateFilter.Choose(s_all, Step(drivers: true, optional: true), new UpdateCheckpoint());

        Assert.Equal(["cu", "dr", "dc", "pr", "be"], selection.ToInstall.Select(update => update.Id));
    }

    [Fact]
    public void AnUpdateThatFailedTwiceIsLeftOut()
    {
        UpdateCheckpoint checkpoint = new();
        checkpoint.Failure("cu", "CU", -1);
        UpdateSelection once = UpdateFilter.Choose([s_cumulative], Step(), checkpoint);
        checkpoint.Failure("cu", "CU", -1);
        UpdateSelection twice = UpdateFilter.Choose([s_cumulative], Step(), checkpoint);

        Assert.Equal([s_cumulative], once.ToInstall);
        Assert.Equal((s_cumulative, UpdateFilter.GaveUp), (Assert.Single(twice.LeftOut).Update, twice.LeftOut[0].Reason));
    }

    [Fact]
    public void AnUpdateThatFailedInThisTurnWaitsForTheRestart()
    {
        UpdateCheckpoint checkpoint = new();
        checkpoint.Failure("cu", "CU", -1);

        UpdateSelection selection = UpdateFilter.Choose([s_cumulative], Step(), checkpoint, new HashSet<string>(["cu"]));

        Assert.Equal((s_cumulative, UpdateFilter.AfterRestart), (Assert.Single(selection.LeftOut).Update, selection.LeftOut[0].Reason));
    }

    [Fact]
    public void NamesAnUpdateWithTheKbNumbersItsTitleLacks()
    {
        Assert.Equal("2026-11 Cumulative Update (KB5070001)", UpdateNames.Of(s_cumulative));
        Assert.Equal("Excluded (KB5070012, KB5070013)", UpdateNames.Of(s_excluded));
        Assert.Equal("Intel - Display - 31.0.101.5590", UpdateNames.Of(s_driver));
    }

    [Fact]
    public void TheCheckpointKeepsWhatWasInstalledAndWhatFailed()
    {
        UpdateCheckpoint checkpoint = new();
        checkpoint.Failure("a", "A", unchecked((int)0x80240022));
        checkpoint.Succeeded("b", "B");

        UpdateCheckpoint read = UpdateCheckpoint.Read(checkpoint.Write());

        Assert.Equal("""{"installed":{"b":"B"},"failed":{"a":{"name":"A","tries":1,"hResult":-2145124318}}}""", checkpoint.Write());
        Assert.Equal("B", read.Installed["B"]);
        Assert.Equal(new FailedUpdate("A", 1, unchecked((int)0x80240022)), read.Failed["A"]);
    }
}
