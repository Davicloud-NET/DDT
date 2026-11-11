// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;
using DDT.Contracts.Values;
using DDT.Server.Deployments;
using Xunit;

namespace DDT.Server.Tests.Applications;

// A multiple choice question asked by value starts with the choices a rule or a default names by label.
public sealed class InputPrefillTests
{
    private static readonly Guid s_editor = Guid.NewGuid();
    private static readonly Guid s_office = Guid.NewGuid();

    private static InputDeclaration Asked => new()
    {
        Name = "Extras",
        Label = "Extras",
        Kind = InputKind.MultiChoice,
        Choices = [new InputChoice(s_editor.ToString("D"), "Code Editor"), new InputChoice(s_office.ToString("D"), "Office Suite")],
    };

    [Fact]
    public void ANameTicksItsEntryWhateverTheCaseAndSpacing()
    {
        Assert.Equal($"{s_office:D};{s_editor:D}", InputPrefills.Of(Asked, " office   SUITE ;code editor"));
    }

    [Fact]
    public void AnIdStaysAndATickIsNotRepeated()
    {
        Assert.Equal($"{s_editor:D}", InputPrefills.Of(Asked, $"Code Editor;{s_editor:D}"));
        Assert.Equal($"{s_editor:D};{s_office:D}", InputPrefills.Of(Asked, $"{s_editor:D};{s_office:D}"));
    }

    // The question shows what it cannot tick as it was given.
    [Fact]
    public void AnEntryThatIsNoChoiceIsLeftAsItIs()
    {
        Assert.Equal($"{s_editor:D};Unknown", InputPrefills.Of(Asked, "Code Editor;Unknown"));
        Assert.Equal("Code Editor", InputPrefills.Of(Asked with { Kind = InputKind.Text, Choices = [] }, "Code Editor"));
    }

    [Fact]
    public void APreviewPrefillsTheInputsItAsks()
    {
        SequenceDefinition definition = new SequenceDefinition(SequenceDefinition.CurrentVersion, []) with { Inputs = [Asked] };
        ResolvedValue other = new("Site", "Vienna", ValueSource.Rule, null, "Site", false);

        IReadOnlyList<ResolvedValue> prefilled = InputPrefills.Of(
            definition,
            [new ResolvedValue("extras", "office suite", ValueSource.Rule, null, "Apps", false), other]);

        Assert.Equal([s_office.ToString("D"), "Vienna"], prefilled.Select(value => value.Value));
    }
}
