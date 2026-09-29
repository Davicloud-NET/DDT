// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.ConsoleProtocol;
using DDT.MachineConsole.Legal;
using DDT.MachineConsole.Texts;
using Xunit;

namespace DDT.MachineConsole.Tests;

// The PO reader, the language the console starts in, and the words and numbers it formats.
public sealed class TextsTests
{
    [Fact]
    public void ReadsAPoCatalogAsLinguiWritesIt()
    {
        const string po = """
            # A comment
            msgid ""
            msgstr ""
            "Language: de\n"

            #: src/somewhere.cs
            msgid "Step {number} of {count}"
            msgstr "Schritt {number} von {count}"

            msgctxt "code"
            msgid "Say \"yes\""
            msgstr ""
            "Sagen Sie "
            "\"ja\"\n"

            msgid "Untranslated"
            msgstr ""
            """;

        PoCatalog catalog = PoCatalog.Read(new StringReader(po));

        Assert.Equal("Schritt {number} von {count}", catalog.Find("Step {number} of {count}"));
        Assert.Equal("Sagen Sie \"ja\"\n", catalog.Find("Say \"yes\""));
        Assert.Null(catalog.Find("Untranslated"));
        Assert.Null(catalog.Find(string.Empty));
        Assert.Throws<FormatException>(() => PoCatalog.Read(new StringReader("msgid \"a\"\nnonsense\n")));
    }

    [Fact]
    public void FallsBackToEnglishForAMessageTheCatalogLacks()
    {
        Localizer german = Localizer.Embedded(UiLanguage.German);

        Assert.Equal("Not a message of the console", german.T("Not a message of the console"));
    }

    [Fact]
    public void FillsNamedPlaceholdersAndLeavesUnknownOnes()
    {
        Assert.Equal("Step 2 of 8", Localizer.Fill("Step {number} of {count}", [("number", "2"), ("count", "8")]));
        Assert.Equal("Step 2 of {count}", Localizer.Fill("Step {number} of {count}", [("number", "2")]));
        Assert.Equal("{ not a placeholder", Localizer.Fill("{ not a placeholder", []));
    }

    [Theory]
    [InlineData(0x0407, UiLanguage.German)]
    [InlineData(0x0C07, UiLanguage.German)]
    [InlineData(0x0807, UiLanguage.German)]
    [InlineData(0x0409, UiLanguage.English)]
    [InlineData(0x040C, UiLanguage.English)]
    public void StartsInTheLanguageOfWindowsPE(int languageId, UiLanguage expected) =>
        Assert.Equal(expected, Catalogs.FromLanguageId((ushort)languageId));

    [Fact]
    public void WritesNumbersAndSizesTheWayTheWebDoes()
    {
        Localizer english = Localizer.Embedded(UiLanguage.English);
        Localizer german = Localizer.Embedded(UiLanguage.German);

        Assert.Equal("476.9 GB", Say.Bytes(english, 512_110_190_592));
        Assert.Equal("476,9 GB", Say.Bytes(german, 512_110_190_592));
        Assert.Equal("1 TB", Say.Bytes(english, 1L << 40));
        Assert.Equal("512 bytes", Say.Bytes(english, 512));
        Assert.Equal("512 Bytes", Say.Bytes(german, 512));
    }

    [Fact]
    public void WritesIdentifiersAsTheMachinesPageDoes()
    {
        Assert.Equal("3C:52:82:6A:1F:0B", Say.Mac("3C52826A1F0B"));
        Assert.Equal("ddt.lab.local:8443", Say.Host("https://ddt.lab.local:8443/"));
        Assert.Equal("ddt.lab.local", Say.Host("https://ddt.lab.local/"));
        Assert.Equal("08:14:00", Say.Time(new DateTimeOffset(2026, 9, 27, 10, 14, 0, TimeSpan.FromHours(2))));
        Assert.Equal("LÄUFT", Say.Tag("Läuft"));
    }

    [Fact]
    public void SaysTheBusAsStorageToolsDo()
    {
        Localizer german = Localizer.Embedded(UiLanguage.German);

        Assert.Equal("NVMe", Say.Bus(german, "Nvme"));
        Assert.Equal("Virtuell", Say.Bus(german, "Virtual"));
        Assert.Equal("SomethingNew", Say.Bus(german, "SomethingNew"));
    }

    [Theory]
    [InlineData(ConsoleStage.Running, "Läuft")]
    [InlineData(ConsoleStage.WaitingForAuthorization, "Wartet auf Freigabe")]
    [InlineData(ConsoleStage.Stopped, "Beendet")]
    public void SaysTheStagesInGerman(ConsoleStage stage, string german) =>
        Assert.Equal(german, Say.Stage(Localizer.Embedded(UiLanguage.German), stage));

    // Each activity has its own words. One that fell through to the last case would say the agent removes itself.
    [Fact]
    public void SaysEveryActivityInWordsOfItsOwn()
    {
        Localizer english = Localizer.Embedded(UiLanguage.English);
        string[] said = [.. Enum.GetValues<ConsoleActivity>().Select(activity => Say.Activity(english, activity))];

        Assert.Equal(said.Length, said.Distinct().Count());
        Assert.Equal("Waiting for answers", Say.Activity(english, ConsoleActivity.WaitingForInput));
        Assert.Equal("Angehalten", Say.Activity(Localizer.Embedded(UiLanguage.German), ConsoleActivity.Paused));
    }

    [Fact]
    public void CarriesEveryLegalTextItNamesAndTheAttributionNoticeWordForWord()
    {
        foreach (LegalTexts.Document document in LegalTexts.Documents)
        {
            Assert.NotEmpty(LegalTexts.Read(document.File));
        }

        // NOTICE opens with the notice its additional term 1 requires.
        Assert.StartsWith(LegalTexts.AttributionNotice + "\n", LegalTexts.Read("NOTICE").ReplaceLineEndings("\n"), StringComparison.Ordinal);
    }

    [Fact]
    public void JoinsTheLinesATextFileBrokeOnlyForWidth()
    {
        IReadOnlyList<string> paragraphs = LegalTexts.Paragraphs(
            "SIL OPEN FONT LICENSE Version 1.1 - 26 February 2007\n-----------------------------------------------------\n\n" +
            "Copyright (c) 2021 Someone\nAll rights reserved.\n\nPermission is hereby granted, free of charge, to any person obtaining\n" +
            "a copy of this software and associated documentation files.\n\n1. Attribution, section 7(b). You must keep\n" +
            "   intact the notices.\n2. Origin, section 7(c). You must not misrepresent the origin\n   of this material.\n");

        Assert.Equal(
            [
                "SIL OPEN FONT LICENSE Version 1.1 - 26 February 2007",
                "-----------------------------------------------------",
                "Copyright (c) 2021 Someone",
                "All rights reserved.",
                "Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files.",
                "1. Attribution, section 7(b). You must keep intact the notices.",
                "2. Origin, section 7(c). You must not misrepresent the origin of this material.",
            ],
            paragraphs);
    }
}
