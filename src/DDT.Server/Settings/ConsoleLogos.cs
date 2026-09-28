// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;
using DDT.Contracts.Settings;
using DDT.Server.Data;
using DDT.Server.Live;
using DDT.Server.Machines;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Settings;

// The logo the console at the machine shows, as the Deployment defaults page shows and replaces it. Machines take it at
// their next registration.
internal sealed class ConsoleLogos(ConsoleLogoStore logos, DdtDbContext database, TimeProvider timeProvider, LiveNotifier live)
{
    // Who uploaded the logo and when come from its latest upload's audit row.
    public async Task<ConsoleLogoView> ViewAsync(CancellationToken cancellationToken)
    {
        if (await logos.CurrentAsync(cancellationToken).ConfigureAwait(false) is not { } logo)
        {
            return new ConsoleLogoView(null, null, null, null, null, null);
        }

        AuditEvent? upload = await database.AuditEvents
            .AsNoTracking()
            .Where(audit => audit.Action == AuditActions.ConsoleLogoUploaded && audit.SubjectId == logo.Sha256)
            .OrderByDescending(audit => audit.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return new ConsoleLogoView(logo.Sha256, logo.Size, logo.Width, logo.Height, upload?.OccurredUtc, upload?.ActorName);
    }

    // Refusal is about the logo field, and set instead of View.
    public async Task<(ConsoleLogoView? View, ServerMessage? Refusal)> UploadAsync(byte[] png, Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(png);

        if (ConsoleLogoStore.Describe(png) is not { } logo)
        {
            return (null, ServerMessages.SettingsConsoleLogoNotPng.With());
        }

        if (logo.Width > ConsoleLogoStore.MaxDimension || logo.Height > ConsoleLogoStore.MaxDimension)
        {
            return (null, ServerMessages.SettingsConsoleLogoDimensions.With("width", logo.Width, "height", logo.Height, "max", ConsoleLogoStore.MaxDimension));
        }

        await logos.SaveAsync(png, cancellationToken).ConfigureAwait(false);
        database.AuditEvents.Add(AuditEvents.Create(
            AuditActions.ConsoleLogoUploaded,
            logo.Sha256,
            actor,
            timeProvider.GetUtcNow(),
            $"Uploaded the console's logo with SHA-256 {logo.Sha256}, {logo.Width} by {logo.Height} pixels, {logo.Size} bytes. Machines show it from their next registration."));
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        ConsoleLogoView view = await ViewAsync(cancellationToken).ConfigureAwait(false);
        live.ConsoleLogoChanged(view);

        return (view, null);
    }

    public async Task<ConsoleLogoView> RemoveAsync(Actor actor, CancellationToken cancellationToken)
    {
        if (await logos.CurrentAsync(cancellationToken).ConfigureAwait(false) is { } logo)
        {
            logos.Delete();
            database.AuditEvents.Add(AuditEvents.Create(
                AuditActions.ConsoleLogoRemoved,
                logo.Sha256,
                actor,
                timeProvider.GetUtcNow(),
                $"Removed the console's logo with SHA-256 {logo.Sha256}. Machines show none from their next registration."));
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        ConsoleLogoView view = await ViewAsync(cancellationToken).ConfigureAwait(false);
        live.ConsoleLogoChanged(view);

        return view;
    }
}
