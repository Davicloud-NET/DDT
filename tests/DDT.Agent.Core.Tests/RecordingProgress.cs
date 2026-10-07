// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Tests;

// Keeps every value reported, on the reporting thread, unlike Progress<T>.
internal sealed class RecordingProgress : IProgress<int>
{
    private readonly Lock _lock = new();
    private readonly List<int> _values = [];

    public List<int> Values
    {
        get
        {
            lock (_lock)
            {
                return [.. _values];
            }
        }
    }

    public void Report(int value)
    {
        lock (_lock)
        {
            _values.Add(value);
        }
    }
}
