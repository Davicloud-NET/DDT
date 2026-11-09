// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Collections.Frozen;

namespace DDT.MachineConsole.Keyboard;

// Finds a layout by any name DDT gives one: a Windows layout id like 0407:00000407 or 00000407, a name of the Linux
// boot environment's text console like uk, or xkb's name like ch(fr).
public static class KeyboardLayouts
{
    public const string Fallback = "us";

    // The .keymap files, by every name that means them.
    private static readonly FrozenDictionary<string, string> s_files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["00000405"] = "cz",
        ["00000406"] = "dk",
        ["00020409"] = "us-intl",
        ["00000813"] = "be",
        ["0000080c"] = "be",
        ["00000809"] = "gb",
        ["00000409"] = "us",
        ["0000040b"] = "fi",
        ["0000040c"] = "fr",
        ["0000100c"] = "ch-fr",
        ["00000407"] = "de",
        ["00000807"] = "ch",
        ["0000040e"] = "hu",
        ["00000410"] = "it",
        ["00000414"] = "no",
        ["00000415"] = "pl",
        ["00000816"] = "pt",
        ["00000416"] = "br",
        ["0000040a"] = "es",
        ["0000041d"] = "se",
        ["0000041f"] = "tr",
        ["be"] = "be",
        ["br"] = "br",
        ["ch"] = "ch",
        ["ch(fr)"] = "ch-fr",
        ["cz"] = "cz",
        ["de"] = "de",
        ["dk"] = "dk",
        ["es"] = "es",
        ["fi"] = "fi",
        ["fr"] = "fr",
        ["gb"] = "gb",
        ["uk"] = "gb",
        ["hu"] = "hu",
        ["it"] = "it",
        ["no"] = "no",
        ["pl"] = "pl",
        ["pt"] = "pt",
        ["se"] = "se",
        ["tr"] = "tr",
        ["us"] = "us",
        ["us(intl)"] = "us-intl",
        ["ch-fr"] = "ch-fr",
        ["us-intl"] = "us-intl",
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    public static IEnumerable<string> All => s_files.Values.Distinct(StringComparer.Ordinal);

    // The layout a name means, read through a head's tables by file name, or null for a name the console doesn't know.
    public static KeyboardLayout? Find(string? name, Func<string, string?> read) =>
        FileOf(name) is string file && read($"{file}.keymap") is string text ? KeyboardLayout.Parse(file, text) : null;

    // The .keymap file for a name, without its extension, or null for one the console doesn't know.
    public static string? FileOf(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        string trimmed = name.Trim();

        // A Windows id with its input locale in front, as the boot image page writes it.
        if (trimmed is [_, _, _, _, ':', .. string layout] && layout.Length == 8)
        {
            trimmed = layout;
        }

        return s_files.GetValueOrDefault(trimmed);
    }
}
