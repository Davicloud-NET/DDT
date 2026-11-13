// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;
using DDT.Core.Sequences;
using Xunit;
using static DDT.Core.Tests.Sequences.TreeSteps;

namespace DDT.Core.Tests.Sequences;

// The Enable roles and features step, as the validator sees it without the machine's DISM.
public sealed class FeatureStepRulesTests
{
    private static EnableFeaturesStep Enable(params string[] features) =>
        new() { Id = Guid.NewGuid(), Name = "Enable roles and features", Features = features };

    private static SequenceDefinition InWindows(SequenceStep step) => Definition(Partition(), ApplyImage(), step);

    [Fact]
    public void AcceptsFeaturesInWindowsAndNeedsVersion7()
    {
        SequenceDefinition definition = InWindows(Enable("NetFx3", "Microsoft-Hyper-V-All", "Printing-PrintToPDFServices-Features", "IIS-ASPNET45"));

        Assert.Empty(Validate(definition));
        Assert.Equal(7, definition.RequiredVersion());
        Assert.Equal("sequence.versionTooLow", Assert.Single(Validate(definition with { Version = 6 })).Code);
    }

    [Fact]
    public void NeedsAFeature()
    {
        EnableFeaturesStep enable = Enable();

        AssertOnly(Validate(InWindows(enable)), enable, "features", "sequence.featuresChoose");
    }

    [Fact]
    public void RefusesNamesDismCannotTakeAndNamesListedTwice()
    {
        EnableFeaturesStep enable = Enable("NetFx3", "Two words", "netfx3", "", "-Leading", "Quote\"d", new string('A', EnableFeaturesStep.MaxNameLength + 1));

        Assert.Equal(
            [
                (enable.Id, "features[1]", "sequence.featureName"),
                (enable.Id, "features[2]", "sequence.featureRepeated"),
                (enable.Id, "features[3]", "sequence.featureName"),
                (enable.Id, "features[4]", "sequence.featureName"),
                (enable.Id, "features[5]", "sequence.featureName"),
                (enable.Id, "features[6]", "sequence.featureName"),
            ],
            Said(Validate(InWindows(enable))));
    }

    [Fact]
    public void EnablesAtMost64()
    {
        EnableFeaturesStep enable = Enable([.. Enumerable.Range(0, EnableFeaturesStep.MaxFeatures + 1).Select(index => $"Feature{index}")]);

        AssertOnly(Validate(InWindows(enable)), enable, "features", "sequence.featuresTooMany");
    }

    [Fact]
    public void RunsInWindowsAndRestartsAtMostThreeTimes()
    {
        EnableFeaturesStep enable = Enable("NetFx3");

        Assert.Equal((SequencePhase.Windows, 3, true), (enable.RequiredPhase, enable.RestartLimit, enable.Resumable));
    }

    [Fact]
    public void BelongsToInstallingWindows()
    {
        EnableFeaturesStep enable = Enable("NetFx3");

        AssertOnly(Validate(WriteRawImage(), enable), enable, null, "sequence.windowsWithRawImage");
    }
}
