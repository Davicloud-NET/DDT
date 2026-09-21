// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Core.Unattend;
using Xunit;

namespace DDT.Core.Tests;

public sealed class ComputerNamesTests
{
    private const string OnlyLettersDigitsAndHyphens = "A computer name can hold only the letters A to Z, digits and hyphens.";

    [Theory]
    [InlineData("PC-0042")]
    [InlineData("A")]
    [InlineData("ws1")]
    [InlineData("0042-PC")]
    [InlineData("ABCDEFGHIJKLMNO")]
    public void AcceptsAValidName(string name)
    {
        Assert.True(ComputerNames.IsValid(name, out string error), error);
        Assert.Empty(error);
    }

    [Theory]
    [InlineData("", "Enter a computer name.")]
    [InlineData("   ", "Enter a computer name.")]
    [InlineData("ABCDEFGHIJKLMNOP", "A computer name can have at most 15 characters.")]
    [InlineData("12345", "A computer name cannot consist of digits only.")]
    [InlineData("-PC42", "A computer name cannot start with a hyphen.")]
    public void RefusesAnInvalidNameWithASentence(string name, string expected)
    {
        Assert.False(ComputerNames.IsValid(name, out string error));
        Assert.Equal(expected, error);
    }

    [Theory]
    [InlineData("PC 42")]
    [InlineData("PC\t42")]
    [InlineData("PC42")]
    [InlineData("Büro-PC")]
    [InlineData("PC​42")]
    [InlineData("PC😀42")]
    [InlineData("PC-́")]
    [InlineData("*")]
    [InlineData("PC.corp")]
    [InlineData("PC_42")]
    [InlineData("PC\\42")]
    [InlineData("PC\"42")]
    [InlineData("PC&42")]
    [InlineData("PC<42")]
    public void RefusesEveryCharacterOutsideTheAllowedSet(string name)
    {
        Assert.False(ComputerNames.IsValid(name, out string error));
        Assert.Equal(OnlyLettersDigitsAndHyphens, error);
    }

    [Fact]
    public void RefusesNull()
    {
        Assert.Throws<ArgumentNullException>(() => ComputerNames.IsValid(null!, out _));
    }
}
