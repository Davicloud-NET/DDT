// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;

namespace DDT.MachineConsole.Texts;

// The catalogs that DDT.MachineConsole.csproj embeds, and the language the console starts in.
public static partial class Catalogs
{
    private const ushort PrimaryLanguageMask = 0x3FF;
    private const ushort German = 0x07;

    public static string ResourceName(string code) => $"locales/{code}/messages.po";

    public static PoCatalog Read(string code)
    {
        using Stream resource = typeof(Catalogs).Assembly.GetManifestResourceStream(ResourceName(code))
            ?? throw new InvalidOperationException($"This console was built without the {code} catalog.");
        using StreamReader reader = new(resource);

        return PoCatalog.Read(reader);
    }

    // The language of Windows PE's user interface. The console runs with the invariant culture, so it asks Windows.
    public static UiLanguage FromWindows()
    {
        try
        {
            return FromLanguageId(GetUserDefaultUILanguage());
        }
        catch (EntryPointNotFoundException)
        {
            return UiLanguage.English;
        }
    }

    public static UiLanguage FromLanguageId(ushort languageId) =>
        (languageId & PrimaryLanguageMask) == German ? UiLanguage.German : UiLanguage.English;

    [LibraryImport("kernel32.dll")]
    private static partial ushort GetUserDefaultUILanguage();
}
