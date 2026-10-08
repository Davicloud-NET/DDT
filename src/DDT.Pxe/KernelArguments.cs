// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers;

namespace DDT.Pxe;

// The words an administrator adds to the Linux boot environment's kernel command line. They go into grub.cfg, which
// GRUB runs as a script, so a word holds nothing GRUB reads as more than a word, and none replaces what DDT sets.
public static class KernelArguments
{
    public const int MaxLength = 256;

    // The start scripts open a root shell at the machine for it. They open one after a failed agent anyway.
    public const string Shell = "ddt.shell";

    private const string OwnPrefix = "ddt.";

    private static readonly SearchValues<char> s_allowed =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_.:,=+/@-");

    // Which program starts in the initramfs, which initrds load and where the kernel came from are DDT's to say.
    private static readonly string[] s_reserved = ["init", "rdinit", "initrd", "BOOT_IMAGE"];

    public static IReadOnlyList<string> Split(string? text) =>
        (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    // The words to write, or none when the text as a whole may not be written.
    public static IReadOnlyList<string> Allowed(string? text)
    {
        IReadOnlyList<string> words = Split(text);

        return (text?.Length ?? 0) <= MaxLength && words.All(word => IsWord(word) && !IsReserved(word)) ? words : [];
    }

    // Letters, digits and _ . : , = + / @ - only, and no hyphen first, which would read as an option.
    public static bool IsWord(string word)
    {
        ArgumentNullException.ThrowIfNull(word);

        return word.Length > 0 && word[0] != '-' && word.AsSpan().IndexOfAnyExcept(s_allowed) < 0;
    }

    public static bool IsReserved(string word)
    {
        ArgumentNullException.ThrowIfNull(word);

        string name = word.Split('=')[0];

        return s_reserved.Contains(name, StringComparer.Ordinal)
            || (name.StartsWith(OwnPrefix, StringComparison.OrdinalIgnoreCase) && !string.Equals(word, Shell, StringComparison.Ordinal));
    }
}
