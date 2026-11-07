// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Core.Sequences;

// The top-level keys of a #cloud-config document, found line by line, because DDT has no YAML reader. It knows a
// block mapping with one key per line, and says Unreadable of a document written any other way.
internal sealed class CloudConfigScan
{
    public const string Header = "#cloud-config";

    private readonly CloudConfigMapping _keys = new();

    private CloudConfigScan()
    {
    }

    // Some line is none of what the scan knows, a flow collection over several lines may end elsewhere than the scan
    // counts, or a key holds a placeholder. The keys found may then not be all there is.
    public bool Unreadable { get; private set; }

    // The key's value, or null if the document does not set it.
    public CloudConfigValue? this[string key] => _keys[key];

    // cloud-init skips white space before the header. An archive and a JSON patch have headers that only start alike.
    public static bool IsCloudConfig(string userData)
    {
        ArgumentNullException.ThrowIfNull(userData);

        string text = userData.TrimStart('\uFEFF').TrimStart();

        return text.StartsWith(Header, StringComparison.Ordinal) && !text.StartsWith(Header + "-", StringComparison.Ordinal);
    }

    // The text comes with the line ends the seed gets, each a line feed.
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
                scan._keys.Set(name, beside, below);
            }
        }

        foreach (string line in userData.TrimStart('\uFEFF').TrimStart().Split('\n').Skip(1))
        {
            // An open flow collection goes on wherever its next line begins. A line in one that reads as a key of the
            // document may be one, if the brackets were counted wrong.
            if (open > 0)
            {
                below.Add(line);
                scan.Unreadable |= CloudConfigText.TryKey(line, out _, out _);
                open += scan.Flow(line);
            }
            else if (BelongsToKey(line, name is not null, beside))
            {
                below.Add(line);
            }
            else if (CloudConfigText.TryKey(line, out string key, out string rest))
            {
                Close();
                (name, beside, below) = (key, rest, []);
                open = rest.StartsWith('{') || rest.StartsWith('[') ? Math.Max(0, scan.Flow(rest)) : 0;

                // A placeholder in a key spells any key once the seed is written, vendor_data among them.
                scan.Unreadable |= key.Contains("{{", StringComparison.Ordinal);
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

    // How many flow collections a line of one opens and leaves open. Brackets inside a quoted scalar that goes on in
    // the next line cannot be told from the collection's own, so the scan gives up there.
    private int Flow(string line)
    {
        int depth = CloudConfigText.Depth(line, out bool quoted);
        Unreadable |= quoted;

        return depth;
    }
}
