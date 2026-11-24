// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;

namespace DDT.Agent.BitLocker;

// Sets nothing, and takes every value as absent.
public sealed class DryRunBitLockerPolicy(AgentLog log) : IBitLockerPolicy
{
    public IReadOnlyList<string> Absent(IEnumerable<string> names) => [.. names];

    public void Set(string name, int value) => log.Information($"Dry run: the BitLocker policy value {name} is not set.");

    public void Remove(IReadOnlyList<string> names)
    {
    }
}
