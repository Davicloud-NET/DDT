// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;

namespace DDT.Agent.Tests;

// Notes which tools were watched, which of them were asked to end and for which the left-over processes were to be
// ended.
internal sealed class RecordingStrays : IStrayProcesses
{
    private readonly Lock _lock = new();
    private readonly List<int> _watched = [];
    private readonly List<string> _asked = [];
    private readonly List<int> _ended = [];

    public List<string> Asked
    {
        get
        {
            lock (_lock)
            {
                return [.. _asked];
            }
        }
    }

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

    public IToolStop WatchFrom(int processId)
    {
        lock (_lock)
        {
            _watched.Add(processId);
        }

        return new Stop(this, processId);
    }

    private sealed class Stop(RecordingStrays strays, int processId) : IToolStop
    {
        public Task AskAsync(string tool)
        {
            lock (strays._lock)
            {
                strays._asked.Add(tool);
            }

            return Task.CompletedTask;
        }

        public Task EndAsync()
        {
            lock (strays._lock)
            {
                strays._ended.Add(processId);
            }

            return Task.CompletedTask;
        }
    }
}
