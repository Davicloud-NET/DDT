// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DDT.Server.BootImage;

// Polls the description next to boot.wim instead of watching it. A network share or a container volume doesn't
// reliably report changes. A new build is pushed, so an open page shows it.
public sealed partial class BootImageWatcher(
    BootImageCatalog catalog,
    BootImagePushes pushes,
    TimeProvider timeProvider,
    ILogger<BootImageWatcher> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(Interval, timeProvider);
        (bool Exists, DateTime LastWriteUtc, long Length) seen = catalog.Stamp();

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            (bool Exists, DateTime LastWriteUtc, long Length) now = catalog.Stamp();

            if (now == seen)
            {
                continue;
            }

            // A failed read must not stop the host, which also runs the web UI and the pxe role. The next tick retries.
            try
            {
                await pushes.ViewChangedAsync(stoppingToken).ConfigureAwait(false);
                seen = now;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                LogLookFailed(exception);
            }
        }
    }

    [LoggerMessage(EventId = 980, Level = LogLevel.Warning, Message = "Could not read the boot image's description after it changed")]
    private partial void LogLookFailed(Exception exception);
}
