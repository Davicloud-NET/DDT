// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent;

// The agent reaches machines alone, in a boot image or as an update, so it carries the legal texts that concern
// it. The start-up lines are the GPL's Appropriate Legal Notices; the LGPL asks for wimlib's copyright among them.
public static class AgentLegalNotices
{
    public const string LicensesArgument = "--licenses";

    // Additional term 1 in NOTICE requires this exact text.
    public const string AttributionNotice = "DDT, the Davicloud Deployment Toolkit. Copyright (C) 2026 Davicloud.";

    // wimlib's COPYING names no holder. This is the line wimlib's own programs print with their version.
    public const string WimlibCopyright = "Copyright 2012-2023 Eric Biggers";

    public static IReadOnlyList<string> StartupLines { get; } =
    [
        AttributionNotice,
        "This program comes with ABSOLUTELY NO WARRANTY.",
        "It is free software: you can redistribute it and modify it under the GNU General Public License, " +
        "version 3 or later, with additional terms.",
        $"It uses wimlib, {WimlibCopyright}, which is under the GNU Lesser General Public License, version 3 or later.",
        $"Run ddt-agent {LicensesArgument} to read the licences.",
    ];

    // Embedded under these names by DDT.Agent.csproj, and printed in this order.
    public static IReadOnlyList<string> Files { get; } =
    [
        "NOTICE",
        "LICENSE",
        "THIRD-PARTY-NOTICES.md",
        "licenses/dotnet/LICENSE.TXT",
        "licenses/dotnet/THIRD-PARTY-NOTICES.TXT",
        "licenses/wimlib/COPYING",
        "licenses/wimlib/COPYING.LGPLv3",
        "licenses/wimlib/COPYING.GPLv3",
        "licenses/wimlib/COPYING.MIT",
        "licenses/wimlib/COPYING.libdivsufsort-lite",
        "licenses/wimlib/COPYING.MinGW-w64-runtime.txt",
    ];

    public static void WriteStartupNotices(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);

        foreach (string line in StartupLines)
        {
            output.WriteLine(line);
        }

        output.WriteLine();
    }

    // A text carried twice, such as the GPL as LICENSE and as wimlib's COPYING.GPLv3, is printed once.
    public static void WriteLicenses(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);

        Dictionary<string, string> printed = new(StringComparer.Ordinal);

        foreach (string file in Files)
        {
            string text = ReadText(file).TrimEnd().ReplaceLineEndings(output.NewLine);

            output.WriteLine($"======== {file} ========");
            output.WriteLine();
            output.WriteLine(printed.TryAdd(text, file) ? text : $"The same text as {printed[text]} above.");
            output.WriteLine();
        }
    }

    public static string ReadText(string file)
    {
        using Stream resource = typeof(AgentLegalNotices).Assembly.GetManifestResourceStream(file)
            ?? throw new InvalidOperationException($"This agent was built without {file}.");
        using StreamReader reader = new(resource);

        return reader.ReadToEnd();
    }
}
