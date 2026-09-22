// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;

namespace DDT.Agent.Tests;

// Firmware variables in memory. Writes and Deletes list the names written and deleted, in order. Every call throws
// Failure when it is set.
internal sealed class FakeUefiVariables : IUefiVariables
{
    public Dictionary<string, byte[]> Values { get; } = new(StringComparer.Ordinal);

    public List<string> Writes { get; } = [];

    public List<string> Deletes { get; } = [];

    public Exception? Failure { get; set; }

    public byte[]? Read(string name)
    {
        ThrowIfFailing();

        return Values.TryGetValue(name, out byte[]? value) ? value : null;
    }

    public void Write(string name, byte[] value)
    {
        ThrowIfFailing();
        Writes.Add(name);
        Values[name] = value;
    }

    public void Delete(string name)
    {
        ThrowIfFailing();
        Deletes.Add(name);
        Values.Remove(name);
    }

    private void ThrowIfFailing()
    {
        if (Failure is { } failure)
        {
            throw failure;
        }
    }
}
