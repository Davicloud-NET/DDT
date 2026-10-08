// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.Extensions.Logging;

namespace DDT.Pxe;

// Looks at the boot files in the background every few seconds, so the loops that receive datagrams answer from memory.
public sealed class NetbootReader(ILinuxBootSite? linux, NetbootChoice? choice, TimeProvider timeProvider, ILogger logger) : IAsyncDisposable
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    private readonly CancellationTokenSource _stopping = new();
    private Task _loop = Task.CompletedTask;
    private bool _failing;

    // One look, on the caller's thread. A look that fails leaves netboot with what the look before found, and the log
    // says so once until one works again.
    public void Read()
    {
        try
        {
            linux?.Refresh();
            choice?.Refresh();

            if (_failing)
            {
                _failing = false;
                PxeLog.BootFilesReadAgain(logger);
            }
        }
        catch (Exception exception)
        {
            // A boundary: the listeners start and go on answering whatever the disk or the server behind it does.
            if (!_failing)
            {
                _failing = true;
                PxeLog.BootFilesReadFailed(logger, exception);
            }
        }
    }

    public void Start() => _loop = RunAsync(_stopping.Token);

    public async Task StopAsync()
    {
        await _stopping.CancelAsync().ConfigureAwait(false);
        await _loop.ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _stopping.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await Task.Delay(Interval, timeProvider, cancellationToken).ConfigureAwait(false);
                Read();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }
}
