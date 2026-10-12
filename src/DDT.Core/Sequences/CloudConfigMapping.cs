// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Core.Sequences;

// The keys of a mapping in a cloud-config document and what each is set to, as CloudConfigScan reads them.
internal sealed class CloudConfigMapping
{
    private readonly Dictionary<string, CloudConfigValue> _members = new(StringComparer.Ordinal);

    // Some key has no name the scan can tell, such as a merge key, so the members found may not be all there is.
    public bool Unsure { get; private set; }

    // The key's value, or null if the mapping does not set it.
    public CloudConfigValue? this[string key] => _members.GetValueOrDefault(key);

    // A key set again keeps its last value, as in YAML, and that value is unsure: the scan follows no quoted scalar
    // over several lines, and one of the two may be text inside one.
    public void Set(string? key, string beside, IReadOnlyList<string> below)
    {
        if (key is null)
        {
            Unsure = true;

            return;
        }

        _members[key] = new CloudConfigValue(beside, below, again: _members.ContainsKey(key));
    }
}
