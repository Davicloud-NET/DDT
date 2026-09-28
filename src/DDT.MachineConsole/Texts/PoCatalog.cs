// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;

namespace DDT.MachineConsole.Texts;

// A PO catalog in the format Lingui writes for the web. Comments, flags, msgctxt and the header are skipped. Plurals
// aren't used.
public sealed class PoCatalog
{
    private readonly Dictionary<string, string> _messages;

    private PoCatalog(Dictionary<string, string> messages) => _messages = messages;

    public IReadOnlyDictionary<string, string> Messages => _messages;

    // The translation, or null if the catalog has none or an empty one.
    public string? Find(string message) =>
        _messages.TryGetValue(message, out string? translated) && translated.Length > 0 ? translated : null;

    public static PoCatalog Read(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        MessageReader messages = new();

        while (reader.ReadLine() is { } line)
        {
            messages.Take(line.Trim());
        }

        messages.Flush();

        return new PoCatalog(messages.Messages);
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

    // The entry being read. A quoted string on its own line continues the last msgid or msgstr.
    private sealed class MessageReader
    {
        private StringBuilder? _id;
        private StringBuilder? _text;
        private StringBuilder? _current;

        public Dictionary<string, string> Messages { get; } = new(StringComparer.Ordinal);

        public void Take(string line)
        {
            if (line.StartsWith('#'))
            {
                return;
            }

            if (line.Length == 0 || line.StartsWith("msgctxt ", StringComparison.Ordinal))
            {
                Flush();
            }
            else if (line.StartsWith("msgid ", StringComparison.Ordinal))
            {
                if (_id is not null)
                {
                    Flush();
                }

                _id = new StringBuilder(Unquote(line["msgid ".Length..]));
                _current = _id;
            }
            else if (line.StartsWith("msgstr ", StringComparison.Ordinal))
            {
                _text = new StringBuilder(Unquote(line["msgstr ".Length..]));
                _current = _text;
            }
            else if (line.StartsWith('"'))
            {
                _current?.Append(Unquote(line));
            }
            else
            {
                throw new FormatException($"A PO catalog has no line like: {line}");
            }
        }

        public void Flush()
        {
            if (_id is not null && _text is not null && _id.Length > 0)
            {
                Messages[_id.ToString()] = _text.ToString();
            }

            _id = null;
            _text = null;
            _current = null;
        }
    }
}
