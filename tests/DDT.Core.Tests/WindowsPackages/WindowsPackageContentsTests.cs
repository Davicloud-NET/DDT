// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Packages;
using DDT.Core.WindowsPackages;
using Xunit;

namespace DDT.Core.Tests.WindowsPackages;

public sealed class WindowsPackageContentsTests
{
    private const string Checkpoint = "windows11.0-kb5043080-x64_953449672073f8fb99badb4cc6d5d7849b9c83e8.msu";
    private const string Cumulative = "windows11.0-kb5132061-x64_aedc878e9e078df2ec7e2cfb98dd5b21831cdbcc.msu";
    private const string LanguagePack = "Microsoft-Windows-Client-Language-Pack_x64_de-de.cab";
    private const string Basic = "Microsoft-Windows-LanguageFeatures-Basic-de-de-Package~31bf3856ad364e35~amd64~~.cab";
    private const string Notepad = "Microsoft-Windows-Notepad-FoD-Package~31bf3856ad364e35~amd64~~.cab";
    private const string NotepadEnglish = "Microsoft-Windows-Notepad-FoD-Package~31bf3856ad364e35~amd64~en-US~.cab";

    [Fact]
    public void AnUpdateWithItsCheckpointListsBothKbNumbersNewestFirst()
    {
        WindowsPackageFacts facts = WindowsPackageContents.Of([Checkpoint, Cumulative]);

        Assert.Equal(WindowsPackageContent.Update, facts.Content);
        Assert.Equal([5132061, 5043080], facts.KbNumbers);
        Assert.Equal("x64", facts.Architecture);
        Assert.Empty(facts.Languages);
    }

    [Fact]
    public void ALanguagePackWithItsFeaturesIsALanguage()
    {
        WindowsPackageFacts facts = WindowsPackageContents.Of([LanguagePack, Basic]);

        Assert.Equal(WindowsPackageContent.Language, facts.Content);
        Assert.Equal(["de-DE"], facts.Languages);
        Assert.Equal("x64", facts.Architecture);
        Assert.Empty(facts.KbNumbers);
    }

    [Fact]
    public void FeaturesOnDemandAreFeaturesWithTheLanguagesOfTheirParts()
    {
        WindowsPackageFacts facts = WindowsPackageContents.Of([Notepad, NotepadEnglish]);

        Assert.Equal(WindowsPackageContent.Features, facts.Content);
        Assert.Equal(["en-US"], facts.Languages);
    }

    [Fact]
    public void UpdatesAndCabinetsTogetherAreMixed() =>
        Assert.Equal(WindowsPackageContent.Mixed, WindowsPackageContents.Of([Cumulative, Notepad]).Content);

    [Fact]
    public void TheArchitectureIsLeftOutWhenTheFilesDisagreeOrOneNamesNone()
    {
        Assert.Null(WindowsPackageContents.Of([Cumulative, "Windows11.0-KB5132061-arm64.msu"]).Architecture);
        Assert.Null(WindowsPackageContents.Of([Cumulative, "update.msu"]).Architecture);
    }
}
