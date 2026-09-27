// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;

namespace DDT.MachineConsole.Texts;

// A catalog in the PO format the web's Lingui catalogs use: msgid is the English message, msgstr its translation,
// with named placeholders such as {name}. Comments, flags, msgctxt and the header are read past; plurals are not used.
public sealed class PoCatalog
{
    private readonly Dictionary<string, string> _messages;

    private PoCatalog(Dictionary<string, string> messages) => _messages = messages;

    public IReadOnlyDictionary<string, string> Messages => _messages;

    // The translation, or null where the catalog has none or an empty one.
    public string? Find(string message) =>
        _messages.TryGetValue(message, out string? translated) && translated.Length > 0 ? translated : null;

    public static PoCatalog Read(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        Dictionary<string, string> messages = new(StringComparer.Ordinal);
        StringBuilder? id = null;
        StringBuilder? text = null;
        StringBuilder? current = null;

        void Flush()
        {
            if (id is not null && text is not null && id.Length > 0)
            {
                messages[id.ToString()] = text.ToString();
            }

            id = null;
            text = null;
            current = null;
        }

        while (reader.ReadLine() is { } raw)
        {
            string line = raw.Trim();

            if (line.Length == 0)
            {
                Flush();
            }
            else if (line.StartsWith('#'))
            {
                continue;
            }
            else if (line.StartsWith("msgctxt ", StringComparison.Ordinal))
            {
                Flush();
                current = null;
            }
            else if (line.StartsWith("msgid ", StringComparison.Ordinal))
            {
                if (id is not null)
                {
                    Flush();
                }

                id = new StringBuilder(Unquote(line["msgid ".Length..]));
                current = id;
            }
            else if (line.StartsWith("msgstr ", StringComparison.Ordinal))
            {
                text = new StringBuilder(Unquote(line["msgstr ".Length..]));
                current = text;
            }
            else if (line.StartsWith('"'))
            {
                current?.Append(Unquote(line));
            }
            else
            {
                throw new FormatException($"A PO catalog has no line like: {line}");
            }
        }

        Flush();

        return new PoCatalog(messages);
    }

    // A quoted PO string, with the C escapes PO uses.
    public static string Unquote(string quoted)
    {
        ArgumentNullException.ThrowIfNull(quoted);

        string value = quoted.Trim();

        if (value.Length < 2 || value[0] != '"' || value[^1] != '"')
        {
            throw new FormatException($"{quoted} is not a quoted PO string.");
        }

        StringBuilder result = new(value.Length);

        for (int index = 1; index < value.Length - 1; index++)
        {
            char character = value[index];

            if (character != '\\' || index + 1 >= value.Length - 1)
            {
                result.Append(character);
                continue;
            }

            index++;
            result.Append(value[index] switch
            {
                'n' => '\n',
                't' => '\t',
                'r' => '\r',
                _ => value[index],
            });
        }

        return result.ToString();
    }
}
