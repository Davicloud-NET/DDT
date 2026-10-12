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
        int end = Find(line, (text, at) => text[at] == '#' && (at == 0 || char.IsWhiteSpace(text[at - 1])), brackets: false, out _);

        return end < 0 ? line : line[..end];
    }

    // How many flow collections the text opens and leaves open, outside quotes and comments. Quoted says that the text
    // ends inside a quoted scalar, which then goes on in the next line.
    public static int Depth(string text, out bool quoted)
    {
        int depth = 0;
        Find(
            StripComment(text),
            (line, at) =>
            {
                depth += line[at] is '{' or '[' ? 1 : line[at] is '}' or ']' ? -1 : 0;

                return false;
            },
            brackets: false,
            out quoted);

        return depth;
    }

    // "key: rest" or "key:" with a plain or a quoted key. Rest comes without its comment.
    public static bool TryKey(string line, out string key, out string rest)
    {
        bool split = TrySplit(line, out string written, out rest);
        key = split ? Name(written) ?? "" : "";

        return key.Length > 0;
    }

    // The same cut for any key, which comes as it is written: a merge key or one with an anchor is a key too.
    public static bool TrySplit(string line, out string written, out string rest)
    {
        int colon = Find(line, (text, at) => text[at] == ':' && (at == text.Length - 1 || char.IsWhiteSpace(text[at + 1])), false, out _);
        (written, rest) = colon < 0 ? ("", "") : (line[..colon], StripComment(line[(colon + 1)..]).Trim());

        return colon >= 0;
    }

    // The name of a key as written. Null for one the scan cannot tell: a merge key, a key with an anchor or a tag, or
    // double quotes with an escape in them.
    public static string? Name(string written)
    {
        bool escaped = written.StartsWith('"') && written.Contains('\\', StringComparison.Ordinal);

        return written.Length == 0 || NotAKey.Contains(written[0], StringComparison.Ordinal) || escaped ? null : Unquote(written);
    }

    // A member of a flow mapping, as "enabled: false", "\"enabled\":false" or a key alone. The key is as written.
    public static (string Key, string Value) FlowMember(string part)
    {
        int colon = Find(part, (text, at) => text[at] == ':', brackets: true, out _);

        return colon < 0 ? (part, "") : (part[..colon].Trim(), part[(colon + 1)..].Trim());
    }

    // The parts of a flow collection's inside, cut at the commas that are its own, in one pass over the text.
    public static List<string> FlowParts(string inside)
    {
        List<string> parts = [];
        int start = 0;

        void Cut(int end)
        {
            if (inside[start..end].Trim() is { Length: > 0 } part)
            {
                parts.Add(part);
            }

            start = end + 1;
        }

        Find(
            inside,
            (text, at) =>
            {
                if (text[at] == ',')
                {
                    Cut(at);
                }

                return false;
            },
            brackets: true,
            out _);
        Cut(inside.Length);

        return parts;
    }

    // A scalar without its quotes and the white space around it.
    public static string Unquote(string text)
    {
        string trimmed = text.Trim();

        return trimmed.Length >= 2 && trimmed[0] is '"' or '\'' && trimmed[^1] == trimmed[0] ? trimmed[1..^1] : trimmed;
    }

    // The first place the test holds outside quotes, and outside nested flow collections when brackets is set. -1 if
    // none. Quoted says that the text ends inside a quoted scalar.
    private static int Find(string text, Func<string, int, bool> test, bool brackets, out bool quoted)
    {
        char quote = '\0';
        int depth = 0;
        int found = -1;

        for (int at = 0; at < text.Length && found < 0; at++)
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
                found = at;
            }
            else if (brackets)
            {
                depth += c is '{' or '[' ? 1 : c is '}' or ']' ? -1 : 0;
            }
        }

        quoted = quote != '\0';

        return found;
    }

    // A quote only opens a scalar where one can begin, so the apostrophe in "don't" opens none. Two single quotes
    // inside a scalar close it and open it again, which reads the same.
    private static bool OpensQuote(string text, int at) =>
        at == 0
        || char.IsWhiteSpace(text[at - 1])
        || "{[,:".Contains(text[at - 1], StringComparison.Ordinal)
        || (text[at] == '\'' && text[at - 1] == '\'');
}
