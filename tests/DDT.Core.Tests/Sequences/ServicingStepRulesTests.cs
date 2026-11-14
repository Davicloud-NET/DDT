// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;
using DDT.Core.Sequences;
using Xunit;
using static DDT.Core.Tests.Sequences.TreeSteps;

namespace DDT.Core.Tests.Sequences;

// The Service packages step, as the validator sees it without the library.
public sealed class ServicingStepRulesTests
{
    private static readonly Guid s_package = Guid.NewGuid();

    private static ServicePackagesStep Service(params Guid[] packages) => new() { Id = Guid.NewGuid(), Name = "Service packages", Packages = packages };

    [Fact]
    public void RunsInWindowsPEAfterTheImageAndNeedsVersion8()
    {
        SequenceDefinition definition = Definition(Partition(), ApplyImage(), Service(s_package), WriteUnattend());

        Assert.Empty(Validate(definition));
        Assert.Equal(8, definition.RequiredVersion());
        Assert.Equal(SequencePhase.WindowsPE, Service().RequiredPhase);
        Assert.Equal("sequence.versionTooLow", Assert.Single(Validate(definition with { Version = 7 })).Code);
    }

    [Fact]
    public void AnAnswerFileMayStartWindowsInALanguageTheStepAdds()
    {
        SequenceDefinition definition = Definition(Partition(), ApplyImage(), Service(s_package), WriteUnattend() with { UiLanguage = "ja-JP" });

        Assert.Empty(Validate(definition));
    }

    [Fact]
    public void ComesAfterTheImage()
    {
        ServicePackagesStep service = Service(s_package);

        AssertOnly(Validate(Partition(), service, ApplyImage()), service, null, "sequence.servicingBeforeImage");
    }

    [Fact]
    public void ComesBeforeTheRestartIntoWindows()
    {
        ServicePackagesStep service = Service(s_package);

        Assert.Contains(
            Validate(Partition(), ApplyImage(), WriteUnattend(), Script(SequencePhase.Windows), service),
            problem => problem.StepId == service.Id);
    }

    [Fact]
    public void NeedsAPackageOrTheMatchingUpdates()
    {
        ServicePackagesStep empty = Service();

        AssertOnly(Validate(Partition(), ApplyImage(), empty), empty, "packages", "sequence.servicingChoose");
        Assert.Empty(Validate(Partition(), ApplyImage(), empty with { MatchingUpdates = true }));
    }

    [Fact]
    public void ListsAPackageOnceAndAtMost64()
    {
        ServicePackagesStep twice = Service(s_package, Guid.NewGuid(), s_package);
        ServicePackagesStep many = Service([.. Enumerable.Range(0, ServicePackagesStep.MaxPackages + 1).Select(_ => Guid.NewGuid())]);

        AssertOnly(Validate(Partition(), ApplyImage(), twice), twice, "packages[2]", "sequence.servicingRepeated");
        AssertOnly(Validate(Partition(), ApplyImage(), many), many, "packages", "sequence.servicingTooMany");
    }

    [Fact]
    public void BelongsToInstallingWindows()
    {
        ServicePackagesStep service = Service(s_package);

        AssertOnly(Validate(WriteRawImage(), service), service, null, "sequence.windowsWithRawImage");
    }
}
