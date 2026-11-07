// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Reflection;

namespace DDT.Agent;

// The legal texts an executable carries and prints: the lines at every start, and the licences of what it is built
// from. The executable embeds each file under the name it is listed by.
public sealed class LegalTexts(Assembly executable, IReadOnlyList<string> startupLines, IReadOnlyList<string> files)
{
    // Additional term 1 in NOTICE requires this exact text.
    public const string AttributionNotice = "DDT, the Davicloud Deployment Toolkit. Copyright (C) 2026 Davicloud.";

    public const string NoWarranty = "This program comes with ABSOLUTELY NO WARRANTY.";

    public const string FreeSoftware =
        "It is free software: you can redistribute it and modify it under the GNU General Public License, " +
        "version 3 or later, with additional terms.";

    public static string HowToReadTheLicences { get; } = $"Run ddt-agent {AgentStartup.LicensesArgument} to read the licences.";

    public void WriteStartupNotices(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);

        foreach (string line in startupLines)
        {
            output.WriteLine(line);
        }

        output.WriteLine();
    }

    // A text carried twice, such as the GPL as LICENSE and as a library's copy of it, is printed once.
    public void WriteLicenses(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);

        Dictionary<string, string> printed = new(StringComparer.Ordinal);

        foreach (string file in files)
        {
            string text = ReadText(file).TrimEnd().ReplaceLineEndings(output.NewLine);

            output.WriteLine($"======== {file} ========");
            output.WriteLine();
            output.WriteLine(printed.TryAdd(text, file) ? text : $"The same text as {printed[text]} above.");
            output.WriteLine();
        }
    }

    public string ReadText(string file)
    {
        using Stream resource = executable.GetManifestResourceStream(file)
            ?? throw new InvalidOperationException($"This agent was built without {file}.");
        using StreamReader reader = new(resource);

        return reader.ReadToEnd();
    }
}
