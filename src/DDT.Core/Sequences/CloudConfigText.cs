// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Core.Sequences;

// The few rules of YAML's text that CloudConfigScan needs: quotes, comments, keys and flow collections.
internal static class CloudConfigText
{
    // What cannot begin a key here: a flow collection, a complex or merge key, an anchor, an alias, a tag, white space.
    private const string NotAKey = "{[?&*!|>%@`,<\t -";

    // The line without its comment. A # starts one at the line's start or after white space, outside quotes.
    public static string StripComment(string line)
    {
        int end = Find(line, (text, at) => text[at] == '#' && (at == 0 || char.IsWhiteSpace(text[at - 1])), brackets: false);

        return end < 0 ? line : line[..end];
    }

    // How many flow collections the text opens and leaves open, outside quotes and comments.
    public static int Depth(string text)
    {
        int depth = 0;
        Find(
            StripComment(text),
            (line, at) =>
            {
                depth += line[at] is '{' or '[' ? 1 : line[at] is '}' or ']' ? -1 : 0;

                return false;
            },
            brackets: false);

        return depth;
    }

    // "key: rest" or "key:" with a plain or a quoted key. Rest comes without its comment.
    public static bool TryKey(string line, out string key, out string rest)
    {
        (key, rest) = ("", "");

        if (line.Length == 0 || NotAKey.Contains(line[0], StringComparison.Ordinal))
        {
            return false;
        }

        int colon = Find(line, (text, at) => text[at] == ':' && (at == text.Length - 1 || char.IsWhiteSpace(text[at + 1])), brackets: false);

        if (colon < 0)
        {
            return false;
        }

        key = Unquote(line[..colon]);
        rest = StripComment(line[(colon + 1)..]).Trim();

        return key.Length > 0;
    }

    // A member of a flow mapping, as "enabled: false", "\"enabled\":false" or a key alone.
    public static (string Key, string Value) FlowMember(string part)
    {
        int colon = Find(part, (text, at) => text[at] == ':', brackets: true);

        return colon < 0 ? (Unquote(part), "") : (Unquote(part[..colon]), part[(colon + 1)..].Trim());
    }

    // The parts of a flow collection's inside, cut at the commas that are its own.
    public static List<string> FlowParts(string inside)
    {
        List<string> parts = [];
        int start = 0;

        while (start <= inside.Length)
        {
            int from = start;
            int comma = Find(inside, (text, at) => at >= from && text[at] == ',', brackets: true);
            int end = comma < 0 ? inside.Length : comma;

            if (inside[start..end].Trim() is { Length: > 0 } part)
            {
                parts.Add(part);
            }

            start = end + 1;
        }

        return parts;
    }

    // A scalar without its quotes and the white space around it.
    public static string Unquote(string text)
    {
        string trimmed = text.Trim();

        return trimmed.Length >= 2 && trimmed[0] is '"' or '\'' && trimmed[^1] == trimmed[0] ? trimmed[1..^1] : trimmed;
    }

    // The first place the test holds outside quotes, and outside nested flow collections when brackets is set. -1 if none.
    private static int Find(string text, Func<string, int, bool> test, bool brackets)
    {
        char quote = '\0';
        int depth = 0;

        for (int at = 0; at < text.Length; at++)
        {
            char c = text[at];

            if (quote != '\0')
            {
                // A backslash escapes in double quotes only.
                at += quote == '"' && c == '\\' ? 1 : 0;
                quote = c == quote ? '\0' : quote;
            }
            else if (c is '"' or '\'' && OpensQuote(text, at))
            {
                quote = c;
            }
            else if (depth == 0 && test(text, at))
            {
                return at;
            }
            else if (brackets)
            {
                depth += c is '{' or '[' ? 1 : c is '}' or ']' ? -1 : 0;
            }
        }

        return -1;
    }

    // A quote only opens a scalar where one can begin, so the apostrophe in "don't" opens none. Two single quotes
    // inside a scalar close it and open it again, which reads the same.
    private static bool OpensQuote(string text, int at) =>
        at == 0
        || char.IsWhiteSpace(text[at - 1])
        || "{[,:".Contains(text[at - 1], StringComparison.Ordinal)
        || (text[at] == '\'' && text[at - 1] == '\'');
}
