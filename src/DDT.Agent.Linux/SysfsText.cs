// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;

namespace DDT.Agent;

// Reads the one-line text files the kernel shows a device's attributes as.
public static class SysfsText
{
    // Null when the attribute is not there, cannot be read or is empty.
    public static string? Read(string path)
    {
        try
        {
            // A device's own strings can hold control characters, which must not reach the console or the server.
            string printable = new([.. File.ReadAllText(path).Select(character => character is >= ' ' and <= '~' ? character : ' ')]);
            string text = string.Join(' ', printable.Split(' ', StringSplitOptions.RemoveEmptyEntries));

            return text.Length == 0 ? null : text;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static long? ReadNumber(string path) =>
        long.TryParse(Read(path), NumberStyles.None, CultureInfo.InvariantCulture, out long number) ? number : null;
}
