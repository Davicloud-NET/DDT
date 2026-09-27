// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Data;
using DDT.Server.Live;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DDT.Server.BootImage;

// A new build arrives by someone copying files into the boot directory, which tells the server nothing. The description
// next to boot.wim is looked at every few seconds rather than watched, because a boot directory on a network share or a
// container volume does not reliably report changes, and a changed one is pushed, so an open page shows the new build.
public sealed partial class BootImageWatcher(
    BootImageCatalog catalog,
    IServiceScopeFactory scopes,
    LiveNotifier live,
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

            // A failed look must not stop the host, which also runs the web UI and the pxe role. The next tick tries again.
            try
            {
                await using AsyncServiceScope scope = scopes.CreateAsyncScope();
                DdtDbContext database = scope.ServiceProvider.GetRequiredService<DdtDbContext>();
                live.BootImageChanged(await catalog.ViewAsync(database, stoppingToken).ConfigureAwait(false));
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
