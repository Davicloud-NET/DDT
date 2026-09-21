// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DDT.Server.Certificates;

// Renews DDT's certificate when it is due, and picks up files another process or an administrator replaced.
public sealed class ServerCertificateRenewer(
    ServerCertificates certificates,
    TimeProvider timeProvider,
    ILogger<ServerCertificateRenewer> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    // The host checked once before it started, so the first check here comes one interval later.
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(Interval, timeProvider);

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            // A failed check must not stop the host; the certificate loaded before stays in service.
            try
            {
                CertificateLog.Checked(logger, certificates, await certificates.CheckAsync(stoppingToken).ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                CertificateLog.CheckFailed(logger, exception);
            }
        }
    }
}
