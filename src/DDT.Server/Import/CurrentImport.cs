// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Import;
using DDT.Server.Live;

namespace DDT.Server.Import;

// The import that runs, or the last one, and what became of each of its files. Kept in memory: the library says what
// an import left, and administrators' pages follow it as it goes.
public sealed class CurrentImport(LiveNotifier live, TimeProvider timeProvider)
{
    // How often a page hears how far a file is. A copy reports every megabyte.
    private static readonly TimeSpan s_progressInterval = TimeSpan.FromMilliseconds(500);

    private readonly Lock _lock = new();
    private ImportStatus? _status;
    private long _pushed;

    public ImportStatus? Status
    {
        get
        {
            lock (_lock)
            {
                return _status;
            }
        }
    }

    // Returns false while an import runs.
    public bool TryBegin(string startedBy, int items)
    {
        lock (_lock)
        {
            if (_status is { State: ImportState.Running })
            {
                return false;
            }

            _status = new ImportStatus(timeProvider.GetUtcNow(), startedBy, ImportState.Running, null, null, 0, 0, items, []);
        }

        Push();

        return true;
    }

    public void Item(string name) => Change(status => status with { Item = name, DoneBytes = 0, TotalBytes = 0 }, always: true);

    public void Progress(long done, long total) => Change(status => status with { DoneBytes = done, TotalBytes = total }, always: false);

    public void Result(ImportResult result) => Change(status => status with { Results = [.. status.Results, result] }, always: true);

    public void Finish() =>
        Change(status => status with { State = ImportState.Finished, FinishedUtc = timeProvider.GetUtcNow(), Item = null }, always: true);

    private void Change(Func<ImportStatus, ImportStatus> change, bool always)
    {
        lock (_lock)
        {
            if (_status is null)
            {
                return;
            }

            _status = change(_status);

            if (!always && timeProvider.GetElapsedTime(_pushed) < s_progressInterval)
            {
                return;
            }
        }

        Push();
    }

    private void Push()
    {
        ImportStatus? status;

        lock (_lock)
        {
            status = _status;
            _pushed = timeProvider.GetTimestamp();
        }

        if (status is not null)
        {
            live.ImportChanged(status);
        }
    }
}
