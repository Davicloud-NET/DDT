// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;
using Xunit;
using static DDT.Core.Tests.Sequences.TreeSteps;

namespace DDT.Core.Tests.Sequences;

// The first-logon commands and the automatic logon of a Write the answer file step.
public sealed class UnattendFirstLogonRulesTests
{
    private static WriteUnattendStep Unattend() => new() { Id = Guid.NewGuid(), Name = "Answer file" };

    private static SequenceDefinition AfterImage(SequenceStep step) => Definition(Partition(), ApplyImage(), step);

    [Fact]
    public void TakesCommandsAndAnAutomaticLogonInVersionSeven()
    {
        WriteUnattendStep step = Unattend() with
        {
            LocalAdministrator = true,
            FirstLogonCommands = ["cmd /c echo one", @"powershell -NoProfile -File C:\Setup\two.ps1"],
            AutoLogon = AutoLogonKind.LocalAdministrator,
            AutoLogonCount = 3,
        };

        Assert.Empty(Validate(AfterImage(step)));
        Assert.Equal(7, step.MinimumVersion);
        Assert.Equal(1, Unattend().MinimumVersion);
    }

    [Fact]
    public void RefusesMoreThanTwentyCommands()
    {
        WriteUnattendStep step = Unattend() with { FirstLogonCommands = [.. Enumerable.Range(1, 21).Select(index => $"cmd /c echo {index}")] };

        AssertOnly(Validate(AfterImage(step)), step, "firstLogonCommands", "sequence.firstLogonCommandsTooMany");
    }

    [Fact]
    public void RefusesAnEmptyCommand()
    {
        WriteUnattendStep step = Unattend() with { FirstLogonCommands = ["cmd /c echo one", " "] };

        AssertOnly(Validate(AfterImage(step)), step, "firstLogonCommands[1]", "sequence.firstLogonCommandEmpty");
    }

    [Fact]
    public void RefusesACommandLongerThanSetupTakes()
    {
        WriteUnattendStep step = Unattend() with { FirstLogonCommands = [new string('x', 1001)] };

        AssertOnly(Validate(AfterImage(step)), step, "firstLogonCommands[0]", "sequence.firstLogonCommandTooLong");
        Assert.Empty(Validate(AfterImage(Unattend() with { FirstLogonCommands = [new string('x', 1000)] })));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void RefusesACountOutsideOneToNinetyNine(int count)
    {
        WriteUnattendStep step = Unattend() with { LocalAdministrator = true, AutoLogon = AutoLogonKind.LocalAdministrator, AutoLogonCount = count };

        AssertOnly(Validate(AfterImage(step)), step, "autoLogonCount", "sequence.autoLogonCount");
    }

    [Fact]
    public void SignsInAsTheLocalAdministratorOnlyWhenTheStepAddsIt()
    {
        WriteUnattendStep step = Unattend() with { AutoLogon = AutoLogonKind.LocalAdministrator };

        AssertOnly(Validate(AfterImage(step)), step, "autoLogon", "sequence.autoLogonNeedsAdministrator");
    }

    [Fact]
    public void SignsInAsAnAccountOnlyWhenTheStepNamesOne()
    {
        WriteUnattendStep step = Unattend() with { AutoLogon = AutoLogonKind.Account };

        AssertOnly(Validate(AfterImage(step)), step, "autoLogonAccount", "sequence.accountChoose");
        Assert.Empty(Validate(AfterImage(step with { AutoLogonAccount = new AccountReference(Guid.NewGuid(), null) })));
    }

    [Fact]
    public void LeavesTheCountAloneWithoutAnAutomaticLogon()
    {
        Assert.Empty(Validate(AfterImage(Unattend() with { AutoLogonCount = 0 })));
    }
}
