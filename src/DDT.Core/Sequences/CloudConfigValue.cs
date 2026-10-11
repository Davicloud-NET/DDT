// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Core.Sequences;

// What a key of a cloud-config document is set to: the text beside the key and the lines below it.
internal sealed class CloudConfigValue(string inline, IReadOnlyList<string> below)
{
    // The lines that say something, each without its comment.
    private readonly string[] _below = [.. below.Select(CloudConfigText.StripComment).Where(line => !string.IsNullOrWhiteSpace(line))];

    // All of the value, without comments.
    public string Text => string.Join('\n', [inline, .. _below]).Trim();

    // An alias, an anchor, a tag, a block scalar or a placeholder: the value is not what its text here says.
    public bool Unsure =>
        Text.Contains("{{", StringComparison.Ordinal)
        || (Text.Length > 0 && "&*!|>".Contains(Text[0], StringComparison.Ordinal))
        || (Text.StartsWith('{') && !Text.EndsWith('}'))
        || (Text.StartsWith('[') && !Text.EndsWith(']'));

    // Nothing, YAML's null, or an empty list or text.
    public bool IsEmpty => Text is "" or "~" or "null" or "Null" or "NULL" or "[]" or "''" or "\"\"";

    // The value as one scalar without its quotes. Null for a mapping or a list.
    public string? Scalar =>
        Text.StartsWith('{') || Text.StartsWith('[') || (inline.Length == 0 && _below.Length > 0 && (IsItem(_below[0]) || IsMember(_below[0])))
            ? null
            : CloudConfigText.Unquote(Text.ReplaceLineEndings(" "));

    // The members of a mapping, in flow or in block style. Null for anything else. A repeated key keeps its last value.
    public IReadOnlyDictionary<string, CloudConfigValue>? Members =>
        Text.StartsWith('{') && Text.EndsWith('}') ? Flow() : inline.Length == 0 && _below.Length > 0 ? Block() : null;

    private static bool IsItem(string line) => line.TrimStart() is "-" || line.TrimStart().StartsWith("- ", StringComparison.Ordinal);

    private static bool IsMember(string line) => CloudConfigText.TryKey(line.TrimStart(), out _, out _);

    private static int Indent(string line) => line.Length - line.TrimStart(' ').Length;

    private Dictionary<string, CloudConfigValue> Flow()
    {
        Dictionary<string, CloudConfigValue> members = new(StringComparer.Ordinal);

        foreach (string part in CloudConfigText.FlowParts(Text[1..^1]))
        {
            (string key, string rest) = CloudConfigText.FlowMember(part);
            members[key] = new CloudConfigValue(rest, []);
        }

        return members;
    }

    // The members are the keys at the first line's indentation. What is indented more belongs to the key above it.
    private Dictionary<string, CloudConfigValue>? Block()
    {
        Dictionary<string, CloudConfigValue> members = new(StringComparer.Ordinal);
        int indent = Indent(_below[0]);
        string? name = null;
        string beside = "";
        List<string> under = [];

        foreach (string line in _below)
        {
            // A list may sit at its key's own indentation.
            if (Indent(line) > indent || (Indent(line) == indent && IsItem(line) && name is not null && beside.Length == 0))
            {
                under.Add(line);

                continue;
            }

            if (Indent(line) < indent || !CloudConfigText.TryKey(line.TrimStart(), out string key, out string rest))
            {
                return null;
            }

            if (name is not null)
            {
                members[name] = new CloudConfigValue(beside, under);
            }

            (name, beside, under) = (key, rest, []);
        }

        if (name is not null)
        {
            members[name] = new CloudConfigValue(beside, under);
        }

        return members;
    }
}
