// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Tests;

// Keeps every step detail reported, on the reporting thread.
internal sealed class RecordingDetails : IProgress<string?>
{
    private readonly Lock _lock = new();
    private readonly List<string?> _values = [];

    public List<string?> Values
    {
        get
        {
            lock (_lock)
            {
                return [.. _values];
            }
        }
    }

    public void Report(string? value)
    {
        lock (_lock)
        {
            _values.Add(value);
        }
    }
}
