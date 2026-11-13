// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;
using DDT.Contracts;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;
using Xunit;
using static DDT.Core.Tests.Sequences.TreeSteps;

namespace DDT.Core.Tests.Sequences;

public sealed class UpdateStepRulesTests
{
    private static WindowsUpdateStep Update(int restarts = WindowsUpdateStep.DefaultRestarts, params string[] excluded) =>
        new() { Id = Guid.NewGuid(), Name = "Windows Update", AllowedRestarts = restarts, ExcludedKbs = excluded };

    private static SequenceDefinition InWindows(SequenceStep step) => Definition(Partition(), ApplyImage(), step);

    [Fact]
    public void AcceptsTheStepInWindowsAndNeedsVersion7()
    {
        SequenceDefinition definition = InWindows(Update(8, "KB5031234", "5031235", " kb5031236 "));

        Assert.Empty(Validate(definition));
        Assert.Equal(7, definition.RequiredVersion());
        Assert.Equal("sequence.versionTooLow", Assert.Single(Validate(definition with { Version = 6 })).Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public void AllowsOneToTwentyRestarts(int restarts)
    {
        WindowsUpdateStep update = Update(restarts);

        AssertOnly(Validate(InWindows(update)), update, "restartLimit", "sequence.updatesRestarts");
    }

    // The engine reads the step's own limit.
    [Fact]
    public void TheStepSaysItsRestartLimit()
    {
        WindowsUpdateStep update = Update(12);

        Assert.Equal((SequencePhase.Windows, 12, true), (update.RequiredPhase, update.RestartLimit, update.Resumable));
        Assert.Equal(8, new WindowsUpdateStep { Id = Guid.NewGuid(), Name = "Windows Update" }.RestartLimit);
    }

    [Fact]
    public void ReadsTheRestartLimitFromTheDocumentAndTakesTheDefaultWithoutIt()
    {
        WindowsUpdateStep read = Assert.IsType<WindowsUpdateStep>(
            JsonSerializer.Deserialize($$"""{"kind":"windowsUpdate","restartLimit":3,"id":"{{Guid.NewGuid()}}","name":"Updates","conditions":[]}""", DdtJsonContext.Default.SequenceStep));
        WindowsUpdateStep missing = Assert.IsType<WindowsUpdateStep>(
            JsonSerializer.Deserialize($$"""{"kind":"windowsUpdate","id":"{{Guid.NewGuid()}}","name":"Updates","conditions":[]}""", DdtJsonContext.Default.SequenceStep));

        Assert.Equal((3, 3), (read.AllowedRestarts, read.RestartLimit));
        Assert.Equal(WindowsUpdateStep.DefaultRestarts, missing.RestartLimit);
        Assert.Empty(Validate(InWindows(missing)));
    }

    [Fact]
    public void RefusesWhatIsNoKbNumberAndNumbersListedTwice()
    {
        WindowsUpdateStep update = Update(8, "KB5031234", "5031234", "KB", "Q123456", "", "KB123456789");

        Assert.Equal(
            [
                (update.Id, "excludedKbs[1]", "sequence.updatesKbRepeated"),
                (update.Id, "excludedKbs[2]", "sequence.updatesKb"),
                (update.Id, "excludedKbs[3]", "sequence.updatesKb"),
                (update.Id, "excludedKbs[4]", "sequence.updatesKb"),
                (update.Id, "excludedKbs[5]", "sequence.updatesKb"),
            ],
            Said(Validate(InWindows(update))));
    }

    [Fact]
    public void LeavesOutAtMost64()
    {
        WindowsUpdateStep update = Update(8, [.. Enumerable.Range(0, WindowsUpdateStep.MaxExcludedKbs + 1).Select(index => $"KB{5000000 + index}")]);

        AssertOnly(Validate(InWindows(update)), update, "excludedKbs", "sequence.updatesTooManyKbs");
    }

    [Fact]
    public void BelongsToInstallingWindows()
    {
        WindowsUpdateStep update = Update();

        AssertOnly(Validate(WriteRawImage(), update), update, null, "sequence.windowsWithRawImage");
    }

    [Theory]
    [InlineData("KB5031234", "5031234")]
    [InlineData("kb5031234", "5031234")]
    [InlineData(" 5031234 ", "5031234")]
    [InlineData("KB 5031234", null)]
    [InlineData("KB123", null)]
    [InlineData(null, null)]
    public void NormalizesKbNumbersToTheirDigits(string? value, string? digits) => Assert.Equal(digits, KbNumbers.Normalize(value));
}
