// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.BitLocker;

namespace DDT.Agent.Tests;

// The policy key as a dictionary that starts with Values. Records "set name" and "remove names" in the shared Events.
internal sealed class FakeBitLockerPolicy(List<string> events) : IBitLockerPolicy
{
    public Dictionary<string, int> Values { get; } = new(StringComparer.Ordinal);

    public IReadOnlyList<string> Absent(IEnumerable<string> names) => [.. names.Where(name => !Values.ContainsKey(name))];

    public void Set(string name, int value)
    {
        events.Add($"set {name}");
        Values[name] = value;
    }

    public void Remove(IReadOnlyList<string> names)
    {
        events.Add($"remove {string.Join(",", names)}");

        foreach (string name in names)
        {
            Values.Remove(name);
        }
    }
}
