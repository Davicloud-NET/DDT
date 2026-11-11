// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;
using DDT.Core.Applications;
using Xunit;

namespace DDT.Core.Tests.Applications;

public sealed class ApplicationNamesTests
{
    [Fact]
    public void CleansSpacesAndComparesWithoutCase()
    {
        Assert.Equal("Visual C++ 2022", ApplicationNames.Clean("  Visual   C++ 2022 "));
        Assert.Equal(ApplicationNames.Normalize("7-zip  23"), ApplicationNames.Normalize(" 7-Zip 23"));
        Assert.Null(ApplicationNames.Normalize("   "));
    }

    [Theory]
    [InlineData("Firefox")]
    [InlineData("  Microsoft  Office 2024 ")]
    [InlineData("Ä Ü Ö, (x64) & more")]
    public void AcceptsAName(string name) => Assert.Null(ApplicationNames.Problem(name));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Line\nbreak")]
    [InlineData("Tab\there")]
    public void RefusesAnEmptyNameOrControlCharacters(string? name) =>
        Assert.Equal(ServerMessages.NameLength.Code, ApplicationNames.Problem(name)?.Code);

    [Fact]
    public void RefusesANameOverTheLimitButCountsItCleaned()
    {
        Assert.Equal(ServerMessages.NameLength.Code, ApplicationNames.Problem(new string('a', 101))?.Code);
        Assert.Null(ApplicationNames.Problem("  " + new string('a', 100) + "  "));
    }

    [Fact]
    public void RefusesASemicolonBecauseNamesTravelInLists() =>
        Assert.Equal(ServerMessages.ApplicationNameSemicolon.Code, ApplicationNames.Problem("Office; Teams")?.Code);
}
