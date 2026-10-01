// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.BootImage;

namespace DDT.Server.BootImage;

// The job that runs, or the last one, with what it wrote. Kept in memory: after a restart there is no job to show,
// and the build it left says the rest.
public sealed class CurrentBootImageJob
{
    // A build writes a few hundred lines. Beyond this only the count goes on.
    public const int MaxLines = 4000;

    private readonly Lock _lock = new();
    private readonly List<string> _lines = [];
    private BootImageJob? _job;
    private int _pushed;

    public BootImageJob? Job
    {
        get
        {
            lock (_lock)
            {
                return _job;
            }
        }
    }

    public BootImageJobLog? Log()
    {
        lock (_lock)
        {
            return _job is null ? null : new BootImageJobLog(_job, [.. _lines]);
        }
    }

    // Returns false while a job runs.
    public bool TryBegin(BootImageJobKind kind, string startedBy, DateTimeOffset now)
    {
        lock (_lock)
        {
            if (_job is { State: BootImageJobState.Running })
            {
                return false;
            }

            _lines.Clear();
            _pushed = 0;
            _job = new BootImageJob(kind, BootImageJobState.Running, now, null, startedBy, null, 0);

            return true;
        }
    }

    public void Append(string line)
    {
        lock (_lock)
        {
            if (_job is null)
            {
                return;
            }

            if (_lines.Count < MaxLines)
            {
                _lines.Add(line);
            }

            _job = _job with { Lines = _job.Lines + 1 };
        }
    }

    public void End(string? problem, DateTimeOffset now)
    {
        lock (_lock)
        {
            if (_job is not null)
            {
                _job = _job with { State = problem is null ? BootImageJobState.Succeeded : BootImageJobState.Failed, FinishedUtc = now, Problem = problem };
            }
        }
    }

    // The lines no page was sent yet, or null when there are none.
    public BootImageJobOutput? TakeUnpushed()
    {
        lock (_lock)
        {
            if (_job is null || _pushed >= _lines.Count)
            {
                return null;
            }

            BootImageJobOutput output = new(_job.StartedUtc, _pushed, _lines.GetRange(_pushed, _lines.Count - _pushed));
            _pushed = _lines.Count;

            return output;
        }
    }
}
