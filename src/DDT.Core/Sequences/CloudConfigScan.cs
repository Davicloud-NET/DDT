// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Core.Sequences;

// The top-level keys of a #cloud-config document, found line by line, because DDT has no YAML reader. It knows a
// block mapping with one key per line, and says Unreadable of a document written any other way.
internal sealed class CloudConfigScan
{
    public const string Header = "#cloud-config";

    private readonly Dictionary<string, CloudConfigValue> _keys = new(StringComparer.Ordinal);

    private CloudConfigScan()
    {
    }

    // Some line at the top level is none of what the scan knows, so the keys it found may not be all there is.
    public bool Unreadable { get; private set; }

    // The key's value, or null if the document does not set it. A repeated key keeps its last value, as in YAML.
    public CloudConfigValue? this[string key] => _keys.GetValueOrDefault(key);

    // cloud-init skips white space before the header. An archive and a JSON patch have headers that only start alike.
    public static bool IsCloudConfig(string userData)
    {
        ArgumentNullException.ThrowIfNull(userData);

        string text = userData.TrimStart('﻿').TrimStart();

        return text.StartsWith(Header, StringComparison.Ordinal) && !text.StartsWith(Header + "-", StringComparison.Ordinal);
    }

    public static CloudConfigScan Read(string userData)
    {
        ArgumentNullException.ThrowIfNull(userData);

        CloudConfigScan scan = new();
        string? name = null;
        string beside = "";
        List<string> below = [];
        int open = 0;

        void Close()
        {
            if (name is not null)
            {
                scan._keys[name] = new CloudConfigValue(beside, below);
            }
        }

        foreach (string line in userData.TrimStart('﻿').TrimStart().Split('\n').Skip(1).Select(line => line.TrimEnd('\r')))
        {
            // An open flow collection goes on wherever its next line begins.
            if (open > 0 || BelongsToKey(line, name is not null, beside))
            {
                below.Add(line);
                open += open > 0 ? CloudConfigText.Depth(line) : 0;
            }
            else if (CloudConfigText.TryKey(line, out string key, out string rest))
            {
                Close();
                (name, beside, below) = (key, rest, []);
                open = rest.StartsWith('{') || rest.StartsWith('[') ? Math.Max(0, CloudConfigText.Depth(rest)) : 0;
            }
            else if (!Says(line) || (line.TrimEnd() == "---" && name is null))
            {
                // A blank line, a comment, or the mark that starts the document.
            }
            else
            {
                scan.Unreadable = true;
            }
        }

        Close();
        scan.Unreadable |= open > 0;

        return scan;
    }

    // Not a blank line and not a comment, which may be indented.
    private static bool Says(string line) => line.TrimStart() is { Length: > 0 } text && text[0] != '#';

    // An indented line, and a list item at the left edge under a key with nothing beside it, are part of that key.
    private static bool BelongsToKey(string line, bool inKey, string beside) =>
        inKey
        && Says(line)
        && (line[0] == ' ' || (beside.Length == 0 && (line == "-" || line.StartsWith("- ", StringComparison.Ordinal))));
}
