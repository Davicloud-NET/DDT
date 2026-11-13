// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;
using DDT.Core.Sequences;
using Xunit;
using static DDT.Core.Tests.Sequences.TreeSteps;

namespace DDT.Core.Tests.Sequences;

// The Write the answer file step's own settings.
public sealed class UnattendStepRulesTests
{
    private static WriteUnattendStep Unattend() => new() { Id = Guid.NewGuid(), Name = "Answer file" };

    private static SequenceDefinition AfterImage(SequenceStep step) => Definition(Partition(), ApplyImage(), step);

    // The server makes the answer file, so an agent of any version runs the step.
    [Theory]
    [MemberData(nameof(NewSettings))]
    public void TakesTheNewSettingsWithoutANewVersion(WriteUnattendStep step)
    {
        SequenceDefinition definition = AfterImage(step);

        Assert.Empty(Validate(definition));
        Assert.Equal(1, step.MinimumVersion);
    }

    public static TheoryData<WriteUnattendStep> NewSettings() =>
    [
        Unattend() with { UiLanguage = "de-DE" },
        Unattend() with { Organization = "Davicloud" },
        Unattend() with { Owner = "{{ComputerName}}" },
        Unattend() with { HideEulaPage = false },
        Unattend() with { HideOemRegistration = false },
        Unattend() with { HideOnlineAccounts = false },
        Unattend() with { HideWirelessSetup = false },
    ];

    [Fact]
    public void RefusesADisplayLanguageThatIsNoLanguageTag()
    {
        WriteUnattendStep step = Unattend() with { UiLanguage = "German" };

        AssertOnly(Validate(AfterImage(step)), step, "uiLanguage", "sequence.uiLanguage");
    }

    [Fact]
    public void LeavesATemplatedDisplayLanguageToTheRun()
    {
        Assert.Empty(Validate(AfterImage(Unattend() with { UiLanguage = "{{Language}}" })));
    }

    [Theory]
    [InlineData("organization")]
    [InlineData("owner")]
    public void RefusesAnOrganizationOrOwnerSetupCannotTake(string field)
    {
        string text = new('A', 257);
        WriteUnattendStep step = field == "owner" ? Unattend() with { Owner = text } : Unattend() with { Organization = text };

        AssertOnly(Validate(AfterImage(step)), step, field, "sequence.registeredTooLong");
    }
}
