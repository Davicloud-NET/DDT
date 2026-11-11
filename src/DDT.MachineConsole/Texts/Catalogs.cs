// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.MachineConsole.Texts;

// The catalogs that DDT.MachineConsole.csproj embeds.
public static class Catalogs
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

    // A Windows language id, such as GetUserDefaultUILanguage gives.
    public static UiLanguage FromLanguageId(ushort languageId) =>
        (languageId & PrimaryLanguageMask) == German ? UiLanguage.German : UiLanguage.English;
}
