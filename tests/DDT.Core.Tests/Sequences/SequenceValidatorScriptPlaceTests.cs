// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;
using DDT.Core.Sequences;
using Xunit;
using static DDT.Core.Tests.Sequences.TreeSteps;

namespace DDT.Core.Tests.Sequences;

// The web starts a new script of a Linux sequence in the phase its place takes (scriptPlace.ts). These are the places
// of its tests, each with the script in the phase the web chooses there, and the validator has nothing to say of any.
public sealed class SequenceValidatorScriptPlaceTests
{
    private static readonly StepCondition s_onOneModel = new(MachineVariableNames.Model, ConditionOperator.Equals, "Latitude 5440");

    private static RunScriptStep New(SequencePhase phase) =>
        Shell() with { Phase = phase, Name = "New script", RebootExitCodes = phase == SequencePhase.Linux ? [194] : [] };

    private static RunScriptStep InBoot() => New(SequencePhase.LinuxBoot);

    private static RunScriptStep Installed() => New(SequencePhase.Linux);

    private static void AssertAccepted(params SequenceStep[] steps) =>
        Assert.Empty(SequenceValidator.Analyse(Definition(steps)).Problems.Select(problem => $"{problem.Field}: {problem.Message}"));

    [Fact]
    public void TakesAScriptInTheLinuxBootEnvironmentUntilASeedStepIsWrittenBeforeIt()
    {
        AssertAccepted(InBoot(), WriteRawImage(), WriteSeed());
        AssertAccepted(WriteRawImage(), InBoot(), WriteSeed());
        AssertAccepted(WriteRawImage(), InBoot());
    }

    [Fact]
    public void TakesAScriptInTheInstalledLinuxAfterTheSeedAlsoInsideAGroupOrAnIfThatComesLater()
    {
        AssertAccepted(WriteRawImage(), WriteSeed(), Installed());
        AssertAccepted(WriteRawImage(), WriteSeed(), Group(Installed()), If([Shell() with { Phase = SequencePhase.Linux }]));
        AssertAccepted(WriteRawImage(), WriteSeed(), Group(Pause()), If([Installed()]));
        AssertAccepted(WriteRawImage(), WriteSeed(), Group(Pause()), If([Pause()], [Installed()]));
        AssertAccepted(WriteRawImage(), WriteSeed(), Group(Pause()), If([Pause()]), Installed());
    }

    [Fact]
    public void CountsASeedInsideAGroupAndInsideAnIfOnlyWhenBothSidesWriteIt()
    {
        AssertAccepted(WriteRawImage(), Group(WriteSeed()), Installed());
        AssertAccepted(WriteRawImage(), Group(WriteSeed(), Installed()));
        AssertAccepted(WriteRawImage(), If([WriteSeed()], [WriteSeed()]), Installed());
        AssertAccepted(WriteRawImage(), If([WriteSeed()]), InBoot());
        AssertAccepted(WriteRawImage(), If([WriteSeed(), Installed()]));
        AssertAccepted(WriteRawImage(), If([WriteSeed()], [InBoot()]));

        // The other choice is refused there, which is why the web does not make it.
        Assert.NotEmpty(Validate(WriteRawImage(), If([WriteSeed()]), Installed()));
        Assert.NotEmpty(Validate(WriteRawImage(), If([WriteSeed()], [Installed()])));
    }

    [Fact]
    public void DoesNotCountASeedThatMayBeSkippedOrMayFailWithoutEndingTheRun()
    {
        AssertAccepted(WriteRawImage(), WriteSeed() with { Conditions = [s_onOneModel] }, InBoot());
        AssertAccepted(WriteRawImage(), WriteSeed() with { When = ThinkPad }, InBoot());
        AssertAccepted(WriteRawImage(), WriteSeed() with { ContinueOnError = true }, InBoot());
        AssertAccepted(WriteRawImage(), Group(WriteSeed()) with { ContinueOnError = true }, InBoot());
        AssertAccepted(WriteRawImage(), Group(WriteSeed(), Installed()) with { ContinueOnError = true });

        Assert.NotEmpty(Validate(WriteRawImage(), WriteSeed() with { ContinueOnError = true }, Installed()));
        Assert.NotEmpty(Validate(WriteRawImage(), Group(WriteSeed()) with { ContinueOnError = true }, Installed()));
    }

    [Fact]
    public void KeepsAScriptInTheLinuxBootEnvironmentWhileALaterStepStillRunsThere()
    {
        AssertAccepted(WriteRawImage(), WriteSeed(), InBoot(), Shell());
        AssertAccepted(WriteRawImage(), WriteSeed(), Group(InBoot()), Shell());
        AssertAccepted(WriteRawImage(), WriteSeed(), InBoot(), Group(Shell()));
        AssertAccepted(WriteRawImage(), WriteSeed(), Shell(), Installed());
        AssertAccepted(WriteRawImage(), WriteSeed(), If([Installed()], [Shell()]));

        Assert.NotEmpty(Validate(WriteRawImage(), WriteSeed(), Installed(), Shell()));
    }

    [Fact]
    public void GoesOnInTheInstalledLinuxOnceAStepBeforeItRunsThere()
    {
        AssertAccepted(WriteRawImage(), WriteSeed(), InLinux(), Installed());
        AssertAccepted(WriteRawImage(), WriteSeed(), Group(InLinux()), Repeat(Installed()));

        Assert.NotEmpty(Validate(WriteRawImage(), WriteSeed(), InLinux(), InBoot()));
    }

    [Fact]
    public void KeepsARepeatInThePhaseItStartsIn()
    {
        AssertAccepted(WriteRawImage(), WriteSeed(), Repeat(InBoot()));
        AssertAccepted(WriteRawImage(), WriteSeed(), Repeat(Group(InBoot())));
        AssertAccepted(WriteRawImage(), WriteSeed(), InLinux(), Repeat(Installed()));

        Assert.NotEmpty(Validate(WriteRawImage(), WriteSeed(), Repeat(Installed())));
    }
}
