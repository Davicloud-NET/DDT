// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Applications;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;
using Xunit;
using static DDT.Core.Tests.Sequences.TreeSteps;

namespace DDT.Core.Tests.Sequences;

// The Install applications step and the Applications input, as the validator sees them without the library.
public sealed class ApplicationStepRulesTests
{
    private static readonly ApplicationReference s_office = new(Guid.NewGuid(), ApplicationReferenceKind.Bundle);
    private static readonly ApplicationReference s_sevenZip = new(Guid.NewGuid(), ApplicationReferenceKind.Application);

    private static InstallApplicationsStep Install(params ApplicationReference[] applications) =>
        new() { Id = Guid.NewGuid(), Name = "Install applications", Applications = applications };

    private static SequenceDefinition InWindows(SequenceStep step, params InputDeclaration[] inputs) =>
        Definition(Partition(), ApplyImage(), step) with { Inputs = inputs.Length > 0 ? inputs : null };

    private static InputDeclaration Offering(params ApplicationReference[] offered) =>
        new() { Name = "Extras", Label = "Extras", Kind = InputKind.Applications, Offered = offered, Ticked = [offered[0].Id] };

    [Fact]
    public void AcceptsApplicationsAndBundlesInWindowsAndNeedsVersion6()
    {
        SequenceDefinition definition = InWindows(Install(s_office, s_sevenZip));

        Assert.Empty(Validate(definition));
        Assert.Equal(6, definition.RequiredVersion());
        Assert.Equal(6, definition.Normalised().Version);
    }

    [Fact]
    public void NeedsAListOrAValueThatNamesThem()
    {
        InstallApplicationsStep install = Install() with { Variable = " " };

        AssertOnly(Validate(InWindows(install)), install, "applications", "sequence.applicationsChoose");
    }

    [Fact]
    public void LeavesAValueNothingDeclaresToTheServer()
    {
        SequenceAnalysis analysis = SequenceValidator.Analyse(InWindows(Install() with { Variable = "Extras" }));

        Assert.Empty(analysis.Problems);
        Assert.Equal(["Extras"], analysis.ValueNames);
    }

    [Fact]
    public void TakesTheAnswerOfAnApplicationsInputAsTheValue()
    {
        SequenceAnalysis analysis = SequenceValidator.Analyse(InWindows(Install() with { Variable = "extras" }, Offering(s_office)));

        Assert.Empty(analysis.Problems);
        Assert.Empty(analysis.ValueNames);
    }

    [Fact]
    public void RefusesAnAccountInputOrANameTemplatesCannotWriteAsTheValue()
    {
        InstallApplicationsStep account = Install(s_office) with { Variable = "Installer" };
        InstallApplicationsStep spaced = Install(s_office) with { Variable = "Two words" };
        InputDeclaration installer = new() { Name = "Installer", Label = "Installer", Kind = InputKind.Account };

        AssertOnly(Validate(InWindows(account, installer)), account, "variable", "sequence.accountInputAsValue");
        AssertOnly(Validate(InWindows(spaced)), spaced, "variable", "sequence.valueName");
    }

    [Fact]
    public void RefusesAnEntryTwiceAndAnEmptyOne()
    {
        InstallApplicationsStep install = Install(s_office, s_sevenZip, s_office, new ApplicationReference(Guid.Empty, ApplicationReferenceKind.Application));

        Assert.Equal(
            [
                (install.Id, "applications[2]", "sequence.applicationRepeated"),
                (install.Id, "applications[3]", "sequence.applicationEmpty"),
            ],
            Said(Validate(InWindows(install))));
    }

    [Fact]
    public void InstallsAtMost256()
    {
        InstallApplicationsStep install = Install(
            [.. Enumerable.Range(0, SequenceValidator.MaxApplications + 1).Select(_ => new ApplicationReference(Guid.NewGuid(), ApplicationReferenceKind.Application))]);

        AssertOnly(Validate(InWindows(install)), install, "applications", "sequence.applicationsTooMany");
    }

    [Fact]
    public void RestartsAndResumesInWindows()
    {
        InstallApplicationsStep install = Install(s_office);

        Assert.Equal((SequencePhase.Windows, 20, true), (install.RequiredPhase, install.RestartLimit, install.Resumable));
    }

    [Fact]
    public void AcceptsAnApplicationsInputThatNeedsVersion6()
    {
        SequenceDefinition definition = Definition(Partition(), ApplyImage()) with { Inputs = [Offering(s_office, s_sevenZip)] };

        Assert.Empty(Validate(definition));
        Assert.Equal(6, definition.RequiredVersion());
        Assert.Equal(
            "sequence.versionTooLow",
            Assert.Single(Validate(definition with { Version = 5 })).Code);
    }

    [Fact]
    public void RefusesAnApplicationsInputThatOffersNothing() =>
        AssertOnly(
            Validate(Definition(Partition(), ApplyImage()) with { Inputs = [Offering(s_office) with { Offered = [], Ticked = [] }] }),
            null,
            "inputs[0].offered",
            "sequence.inputOffered");

    [Fact]
    public void RefusesATickThatIsNotOfferedAndTheMembersOfOtherKinds()
    {
        InputDeclaration input = Offering(s_office) with
        {
            Offered = [s_office, s_office],
            Ticked = [s_sevenZip.Id],
            Choices = [new InputChoice("Office")],
            Default = "Office",
            MaxLength = 10,
        };

        (Guid?, string?, string?)[] expected =
        [
            (null, "inputs[0].offered[1]", "sequence.applicationRepeated"),
            (null, "inputs[0].ticked[0]", "sequence.inputTickedNotOffered"),
            (null, "inputs[0].choices", "sequence.inputNotForApplications"),
            (null, "inputs[0].default", "sequence.inputNotForApplications"),
            (null, "inputs[0].maxLength", "sequence.inputNotForApplications"),
        ];

        Assert.Equal(expected, Said(Validate(Definition(Partition(), ApplyImage()) with { Inputs = [input] })));
    }

    [Fact]
    public void RefusesApplicationsOfferedByAnotherKind() =>
        AssertOnly(
            Validate(Definition(Partition(), ApplyImage()) with { Inputs = [Offering(s_office) with { Kind = InputKind.MultiChoice, Choices = [new InputChoice("A")] }] }),
            null,
            "inputs[0].offered",
            "sequence.inputOfferedOnlyApplications");
}
