// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Certificates;
using DDT.Server.Data;
using DDT.Server.Live;
using DDT.Server.Machines;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DDT.Server.Settings;

// Records when nobody confirmed a pair the page installed and DDT went back to the previous pair. It's audited like the
// page's other certificate changes, with DDT as the actor, and pushed to the administrators' pages.
public sealed partial class CertificateRollbackRecorder(
    IServiceProvider services,
    IServiceScopeFactory scopes,
    LiveNotifier live,
    TimeProvider timeProvider,
    ILogger<CertificateRollbackRecorder> logger) : IHostedService
{
    private ServerCertificates? _certificates;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _certificates = services.GetService<ServerCertificates>();

        if (_certificates is not null)
        {
            _certificates.RolledBack += OnRolledBack;
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (_certificates is not null)
        {
            _certificates.RolledBack -= OnRolledBack;
        }

        return Task.CompletedTask;
    }

    private void OnRolledBack(object? sender, CertificateRolledBackEventArgs rolledBack) => _ = RecordAsync(rolledBack);

    private async Task RecordAsync(CertificateRolledBackEventArgs rolledBack)
    {
        try
        {
            using IServiceScope scope = scopes.CreateScope();
            DdtDbContext database = scope.ServiceProvider.GetRequiredService<DdtDbContext>();

            database.AuditEvents.Add(AuditEvents.Create(
                AuditActions.CertificateRolledBack,
                rolledBack.RolledBackThumbprint,
                new Actor(null, "DDT", null),
                timeProvider.GetUtcNow(),
                $"Nobody confirmed the server certificate {rolledBack.RolledBackThumbprint} within " +
                $"{ServerCertificates.ConfirmWithin.TotalMinutes} minutes, so DDT serves {rolledBack.Restored.Subject}, " +
                $"{rolledBack.Restored.Thumbprint}, again."));
            await database.SaveChangesAsync().ConfigureAwait(false);

            live.CertificateChanged(await scope.ServiceProvider.GetRequiredService<CertificateChanges>()
                .ViewAsync(null, CancellationToken.None)
                .ConfigureAwait(false));
        }
        catch (Exception exception)
        {
            LogRecordFailed(exception);
        }
    }

    [LoggerMessage(EventId = 961, Level = LogLevel.Warning, Message = "Could not record that the server certificate went back to the one before")]
    private partial void LogRecordFailed(Exception exception);
}
