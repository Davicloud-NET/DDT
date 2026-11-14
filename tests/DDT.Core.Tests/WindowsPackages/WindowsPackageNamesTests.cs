// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Core.WindowsPackages;
using Xunit;

namespace DDT.Core.Tests.WindowsPackages;

// Names as the update catalog and the Languages and Optional Features ISO (26100.1) give them.
public sealed class WindowsPackageNamesTests
{
    public static TheoryData<string, WindowsPackageFileKind, string?, string?, int?> RealNames => new()
    {
        { "windows11.0-kb5043080-x64_953449672073f8fb99badb4cc6d5d7849b9c83e8.msu", WindowsPackageFileKind.Update, null, "x64", 5043080 },
        { "windows11.0-kb5132061-x64_aedc878e9e078df2ec7e2cfb98dd5b21831cdbcc.msu", WindowsPackageFileKind.Update, null, "x64", 5132061 },
        { "Windows11.0-KB5132061-ARM64.msu", WindowsPackageFileKind.Update, null, "arm64", 5132061 },
        { "update.msu", WindowsPackageFileKind.Update, null, null, null },
        { "Microsoft-Windows-Client-Language-Pack_x64_de-de.cab", WindowsPackageFileKind.LanguagePack, "de-DE", "x64", null },
        { "Microsoft-Windows-Client-Language-Pack_x64_sr-latn-rs.cab", WindowsPackageFileKind.LanguagePack, "sr-Latn-RS", "x64", null },
        { "Microsoft-Windows-Server-Language-Pack_x64_fr-fr.cab", WindowsPackageFileKind.LanguagePack, "fr-FR", "x64", null },
        { "Microsoft-Windows-Lip-Language-Pack_x64_ca-es.cab", WindowsPackageFileKind.LanguagePack, "ca-ES", "x64", null },
        {
            "Microsoft-Windows-LanguageFeatures-Basic-de-de-Package~31bf3856ad364e35~amd64~~.cab",
            WindowsPackageFileKind.LanguageFeature, "de-DE", "x64", null
        },
        {
            "Microsoft-Windows-LanguageFeatures-TextToSpeech-zh-tw-Package~31bf3856ad364e35~amd64~~.cab",
            WindowsPackageFileKind.LanguageFeature, "zh-TW", "x64", null
        },
        {
            "Microsoft-Windows-LanguageFeatures-OCR-sr-latn-rs-Package~31bf3856ad364e35~amd64~~.cab",
            WindowsPackageFileKind.LanguageFeature, "sr-Latn-RS", "x64", null
        },
        {
            "Microsoft-Windows-LanguageFeatures-Fonts-Jpan-Package~31bf3856ad364e35~amd64~~.cab",
            WindowsPackageFileKind.LanguageFeature, null, "x64", null
        },
        { "Microsoft-Windows-Notepad-FoD-Package~31bf3856ad364e35~amd64~~.cab", WindowsPackageFileKind.Feature, null, "x64", null },
        { "Microsoft-Windows-Notepad-FoD-Package~31bf3856ad364e35~amd64~en-US~.cab", WindowsPackageFileKind.Feature, "en-US", "x64", null },
        {
            "HyperV-OptionalFeature-VirtualMachinePlatform-Client-Disabled-FOD-Package~31bf3856ad364e35~amd64~ar-SA~.cab",
            WindowsPackageFileKind.Feature, "ar-SA", "x64", null
        },
        {
            "Downlevel-NLS-Sorting-Versions-Server-FoD-Package~31bf3856ad364e35~wow64~~.cab",
            WindowsPackageFileKind.Feature, null, "x64", null
        },
        { "FoDMetadata_Client.cab", WindowsPackageFileKind.Feature, null, null, null },
    };

    [Theory]
    [MemberData(nameof(RealNames))]
    public void ReadsWhatAFileIsFromItsName(string name, WindowsPackageFileKind kind, string? language, string? architecture, int? kb)
    {
        WindowsPackageFile? file = WindowsPackageNames.Read(name);

        Assert.Equal(new WindowsPackageFile(name, kind, language, architecture, kb), file);
    }

    [Theory]
    [InlineData("setup.exe")]
    [InlineData("readme.txt")]
    [InlineData("update.msu.zip")]
    public void AFileThatIsNoMsuOrCabIsNoPackageFile(string name)
    {
        Assert.Null(WindowsPackageNames.Read(name));
        Assert.False(WindowsPackageNames.IsPackageFile(name));
    }

    [Theory]
    [InlineData("de-de", "de-DE")]
    [InlineData("EN-us", "en-US")]
    [InlineData("sr-latn-rs", "sr-Latn-RS")]
    [InlineData("es-419", "es-419")]
    public void WritesALanguageTagTheWayWindowsDoes(string tag, string expected) =>
        Assert.Equal(expected, WindowsPackageNames.Language(tag));
}
