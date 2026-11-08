// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;

namespace DDT.MachineConsole.Texts;

// The language the console starts in: Windows PE's. The console runs with the invariant culture, so it asks Windows.
public static partial class WindowsLanguage
{
    public static UiLanguage Current()
    {
        try
        {
            return Catalogs.FromLanguageId(GetUserDefaultUILanguage());
        }
        catch (EntryPointNotFoundException)
        {
            return UiLanguage.English;
        }
    }

    [LibraryImport("kernel32.dll")]
    private static partial ushort GetUserDefaultUILanguage();
}
