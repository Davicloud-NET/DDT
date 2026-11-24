// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DiscUtils.Iso9660;

namespace DDT.Server.Tests.WindowsPackages;

// A small Languages and Optional Features ISO with the real layout and names: German, Japanese and a language interface
// pack, a feature on demand, and the Windows PE folder the import leaves alone.
internal static class TestLanguageIso
{
    public const string GermanPack = "Microsoft-Windows-Client-Language-Pack_x64_de-de.cab";
    public const string GermanBasic = "Microsoft-Windows-LanguageFeatures-Basic-de-de-Package~31bf3856ad364e35~amd64~~.cab";
    public const string GermanSpeech = "Microsoft-Windows-LanguageFeatures-Speech-de-de-Package~31bf3856ad364e35~amd64~~.cab";
    public const string JapanesePack = "Microsoft-Windows-Client-Language-Pack_x64_ja-jp.cab";
    public const string JapaneseOcr = "Microsoft-Windows-LanguageFeatures-OCR-ja-jp-Package~31bf3856ad364e35~amd64~~.cab";
    public const string JapaneseFonts = "Microsoft-Windows-LanguageFeatures-Fonts-Jpan-Package~31bf3856ad364e35~amd64~~.cab";
    public const string CatalanPack = "Microsoft-Windows-Lip-Language-Pack_x64_ca-es.cab";
    public const string Notepad = "Microsoft-Windows-Notepad-FoD-Package~31bf3856ad364e35~amd64~de-DE~.cab";

    public static readonly string[] Cabinets = [CatalanPack, GermanPack, JapanesePack, GermanBasic, GermanSpeech, JapaneseFonts, JapaneseOcr, Notepad];

    // Each cabinet holds its own name, so the tests can tell them apart.
    public static string Write(string path)
    {
        CDBuilder builder = new() { UseJoliet = true, VolumeIdentifier = "LOF" };

        foreach (string cabinet in Cabinets)
        {
            builder.AddFile($@"LanguagesAndOptionalFeatures\{cabinet}", Content(cabinet));
        }

        builder.AddFile(@"LanguagesAndOptionalFeatures\metadata\DesktopTargetCompDBForISO_LP_de-de.xml.cab", [1, 2]);
        builder.AddFile(@"Windows Preinstallation Environment\x64\WinPE_OCs\WinPE-WMI.cab", [3, 4]);
        builder.Build(path);

        return path;
    }

    public static byte[] Content(string cabinet) => System.Text.Encoding.UTF8.GetBytes(cabinet);
}
