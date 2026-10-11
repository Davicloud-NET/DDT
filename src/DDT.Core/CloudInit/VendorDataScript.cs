// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;

namespace DDT.Core.CloudInit;

// The NoCloud vendor-data of a run that goes on in the installed Linux: a shell script that cloud-init runs once per
// instance. It installs the agent from the seed and starts it as a service. The text is vendor-data.sh beside this file.
public static class VendorDataScript
{
    public const string ResourceName = "cloud-init/vendor-data.sh";

    // The script's own line, by which the agent knows a copy of it when it removes itself.
    public const string Marker = "# DDT: goes on with this machine's task sequence. Written by the DDT agent, removed when the run ends.";

    // A script is small, so a larger file is none of DDT's.
    private const int MaxBytes = 64 * 1024;

    private static string? s_text;

    // Read at the first use, so a build that lacks the file says so where the script is asked for.
    public static string Text => s_text ??= Unix(Read());

    // As it goes onto the seed. The shell takes a carriage return as part of a word, and #! must be the first bytes.
    public static byte[] Bytes() => Encoding.UTF8.GetBytes(Text);

    // With LF line ends and no byte order mark, however the file was checked out.
    public static string Unix(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return text.TrimStart('﻿').Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    // Whether content is this script, as the seed holds it or as cloud-init copied it.
    public static bool IsOurs(ReadOnlySpan<byte> content) =>
        content.Length <= MaxBytes && content.IndexOf(Encoding.ASCII.GetBytes(Marker)) >= 0;

    private static string Read()
    {
        using Stream resource = typeof(VendorDataScript).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"{ResourceName} is not embedded in DDT.Core.");
        using MemoryStream bytes = new();
        resource.CopyTo(bytes);

        return Encoding.UTF8.GetString(bytes.ToArray());
    }
}
