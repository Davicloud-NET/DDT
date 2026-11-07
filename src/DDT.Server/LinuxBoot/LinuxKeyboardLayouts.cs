// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.LinuxBoot;

// The keyboard layouts of the Linux boot environment's text console, by the names of its keymap files.
public static class LinuxKeyboardLayouts
{
    public const string Default = "us";

    private static readonly Dictionary<string, string> s_names = new(StringComparer.Ordinal)
    {
        ["us"] = "English (United States)",
        ["uk"] = "English (United Kingdom)",
        ["de"] = "German",
        ["ch"] = "German (Switzerland)",
        ["fr"] = "French",
        ["it"] = "Italian",
        ["es"] = "Spanish",
    };

    // The layout the settings name when the environment has it, and its own default otherwise.
    public static string Keymap(string? configured, IReadOnlyList<string> available) =>
        configured?.Trim() is { Length: > 0 } keymap && available.Contains(keymap, StringComparer.Ordinal) ? keymap : Default;

    // What the agent shows at the sign-in, such as "German (de)".
    public static string Name(string keymap) => s_names.TryGetValue(keymap, out string? name) ? $"{name} ({keymap})" : keymap;
}
