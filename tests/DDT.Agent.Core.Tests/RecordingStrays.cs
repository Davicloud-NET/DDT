// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;

namespace DDT.Agent.Tests;

// Notes which tools were watched and for which of them the left-over processes were to be ended.
internal sealed class RecordingStrays : IStrayProcesses
{
    private readonly Lock _lock = new();
    private readonly List<int> _watched = [];
    private readonly List<int> _ended = [];

    public List<int> Watched
    {
        get
        {
            lock (_lock)
            {
                return [.. _watched];
            }
        }
    }

    public List<int> Ended
    {
        get
        {
            lock (_lock)
            {
                return [.. _ended];
            }
        }
    }

    public Action WatchFrom(int processId)
    {
        lock (_lock)
        {
            _watched.Add(processId);
        }

        return () =>
        {
            lock (_lock)
            {
                _ended.Add(processId);
            }
        };
    }
}
