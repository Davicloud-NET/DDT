// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;
using DDT.MachineConsole.Texts;

namespace DDT.MachineConsole.Legal;

// The licence texts that DDT.MachineConsole.csproj embeds under these names. The console ships to machines by itself,
// inside a boot image, so it has to carry the texts its licences require.
public static class LegalTexts
{
    // A line this short ends on purpose, like a heading or a copyright line.
    private const int ShortLine = 40;

    // Additional term 1 in NOTICE requires this exact text.
    public const string AttributionNotice = "DDT, the Davicloud Deployment Toolkit. Copyright (C) 2026 Davicloud.";

    public static IReadOnlyList<Document> Documents { get; } =
    [
        new("NOTICE"),
        new("LICENSE"),
        new("licenses/avalonia/LICENSE.md"),
        new("licenses/avalonia/NOTICE.md"),
        new("licenses/microcom/LICENSE"),
        new("licenses/skiasharp/LICENSE.txt"),
        new("licenses/skiasharp/THIRD-PARTY-NOTICES.txt"),
        new("licenses/dotnet/LICENSE.TXT"),
        new("licenses/dotnet/THIRD-PARTY-NOTICES.TXT"),
        new("licenses/fonts/Archivo-OFL.txt"),
        new("licenses/fonts/MartianMono-OFL.txt"),
    ];

    public static string Title(Localizer l, Document document)
    {
        ArgumentNullException.ThrowIfNull(l);
        ArgumentNullException.ThrowIfNull(document);

        return document.File switch
        {
            "NOTICE" => l.T("DDT: notice and additional terms"),
            "LICENSE" => l.T("DDT: GNU General Public License, version 3"),
            "licenses/avalonia/LICENSE.md" => l.T("Avalonia: MIT licence"),
            "licenses/avalonia/NOTICE.md" => l.T("Avalonia: software by others in it"),
            "licenses/microcom/LICENSE" => l.T("MicroCom: MIT licence"),
            "licenses/skiasharp/LICENSE.txt" => l.T("SkiaSharp and HarfBuzzSharp: MIT licence"),
            "licenses/skiasharp/THIRD-PARTY-NOTICES.txt" => l.T("Skia, HarfBuzz and what they contain"),
            "licenses/dotnet/LICENSE.TXT" => l.T(".NET: MIT licence"),
            "licenses/dotnet/THIRD-PARTY-NOTICES.TXT" => l.T(".NET: software by others in it"),
            "licenses/fonts/Archivo-OFL.txt" => l.T("Archivo: SIL Open Font License"),
            _ => l.T("Martian Mono: SIL Open Font License"),
        };
    }

    public static string Read(string file)
    {
        using Stream resource = typeof(LegalTexts).Assembly.GetManifestResourceStream(file)
            ?? throw new InvalidOperationException($"This console was built without {file}.");
        using StreamReader reader = new(resource);

        return reader.ReadToEnd();
    }

    // Joins lines that the text file only broke for width, so they wrap to the view. A paragraph also ends at a list
    // item, a rule, a line after a short one like a heading, and an indented line that doesn't continue a list item.
    public static IReadOnlyList<string> Paragraphs(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        List<string> paragraphs = [];
        StringBuilder current = new();
        string previous = string.Empty;
        bool item = false;

        foreach (string line in text.ReplaceLineEndings("\n").TrimEnd().Split('\n'))
        {
            string trimmed = line.Trim();

            if (trimmed.Length == 0)
            {
                End(paragraphs, current);
                previous = string.Empty;
                continue;
            }

            bool indented = char.IsWhiteSpace(line[0]);
            bool rule = trimmed.All(character => character is '-' or '=' or '_' or '*' or '#');

            if (current.Length > 0 && (rule || IsRule(previous) || IsListItem(trimmed) || previous.Length < ShortLine || (indented && !item)))
            {
                End(paragraphs, current);
            }

            if (current.Length == 0)
            {
                item = IsListItem(trimmed);
                current.Append(line.TrimEnd());
            }
            else
            {
                current.Append(' ').Append(trimmed);
            }

            previous = trimmed;
        }

        End(paragraphs, current);

        return paragraphs;
    }

    private static bool IsRule(string line) => line.Length > 0 && line.All(character => character is '-' or '=' or '_' or '*' or '#');

    private static void End(List<string> paragraphs, StringBuilder current)
    {
        if (current.Length > 0)
        {
            paragraphs.Add(current.ToString());
            current.Clear();
        }
    }

    private static bool IsListItem(string line) =>
        line.StartsWith("- ", StringComparison.Ordinal)
        || line.StartsWith("* ", StringComparison.Ordinal)
        || (line.Length > 2 && char.IsAsciiDigit(line[0]) && line[1] is '.' or ')');

    public sealed record Document(string File);
}
