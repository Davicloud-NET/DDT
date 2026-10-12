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

    // The members of a mapping, in flow or in block style. Null for anything else.
    public CloudConfigMapping? Mapping =>
        Text.StartsWith('{') && Text.EndsWith('}') ? Flow() : inline.Length == 0 && _below.Length > 0 ? Block() : null;

    private static bool IsItem(string line) => line.TrimStart() is "-" || line.TrimStart().StartsWith("- ", StringComparison.Ordinal);

    private static bool IsMember(string line) => CloudConfigText.TrySplit(line.TrimStart(), out _, out _);

    private static int Indent(string line) => line.Length - line.TrimStart(' ').Length;

    private CloudConfigMapping Flow()
    {
        CloudConfigMapping members = new();

        foreach (string part in CloudConfigText.FlowParts(Text[1..^1]))
        {
            (string key, string rest) = CloudConfigText.FlowMember(part);
            members.Set(CloudConfigText.Name(key), rest, []);
        }

        return members;
    }

    // The members are the keys at the first line's indentation. What is indented more belongs to the key above it.
    private CloudConfigMapping? Block()
    {
        CloudConfigMapping members = new();
        int indent = Indent(_below[0]);
        (bool Open, string? Name, string Beside, List<string> Under) member = (false, null, "", []);

        foreach (string line in _below)
        {
            // A list may sit at its key's own indentation.
            if (Indent(line) > indent || (Indent(line) == indent && IsItem(line) && member.Open && member.Beside.Length == 0))
            {
                member.Under.Add(line);

                continue;
            }

            // A list, a scalar or a line that is set too far left: no mapping the scan can read.
            if (Indent(line) < indent || IsItem(line) || !CloudConfigText.TrySplit(line.TrimStart(), out string written, out string rest))
            {
                return null;
            }

            if (member.Open)
            {
                members.Set(member.Name, member.Beside, member.Under);
            }

            member = (true, CloudConfigText.Name(written), rest, []);
        }

        members.Set(member.Name, member.Beside, member.Under);

        return members;
    }
}
