// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.MachineConsole.Texts;

// The language the console starts in, from the locale as gettext reads it; English when none is set. The agent's
// language from the server follows with its first message.
public static class LinuxLanguage
{
    private static readonly string[] s_variables = ["LC_ALL", "LC_MESSAGES", "LANG"];

    public static UiLanguage Current()
    {
        foreach (string variable in s_variables)
        {
            if (Environment.GetEnvironmentVariable(variable) is { Length: > 0 } locale)
            {
                return locale.StartsWith("de", StringComparison.OrdinalIgnoreCase) ? UiLanguage.German : UiLanguage.English;
            }
        }

        return UiLanguage.English;
    }
}
