// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Settings;

// Every DDT process on one database has to share the key ring, or it cannot read the secrets the others stored. The
// first process stores a canary, and each later one checks that it can read it.
public sealed class SettingsKeyRing(DdtDbContext database, SettingsProtector protector, DdtSettings settings, TimeProvider timeProvider)
{
    private const int MaxAttempts = 5;

    // Sets DdtSettings.KeyRingReadable. Saves check that flag.
    public async Task<bool> CheckAsync(CancellationToken cancellationToken)
    {
        bool readable = await ReadsCanaryAsync(cancellationToken).ConfigureAwait(false);
        settings.KeyRingReadable = readable;

        return readable;
    }

    private async Task<bool> ReadsCanaryAsync(CancellationToken cancellationToken)
    {
        for (int attempt = 1; ; attempt++)
        {
            SettingsSection? row = await database.SettingsSections
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Section == SettingsSectionNames.KeyRing, cancellationToken)
                .ConfigureAwait(false);

            if (row is not null)
            {
                return protector.ReadsCanary(row);
            }

            DateTimeOffset now = timeProvider.GetUtcNow();
            database.SettingsSections.Add(new SettingsSection
            {
                Section = SettingsSectionNames.KeyRing,
                SchemaVersion = SettingsStore.SchemaVersion,
                Secrets = protector.CanarySecrets(now),
                Version = 1,
                UpdatedUtc = now,
                UpdatedByName = Actor.Configuration.Name,
            });

            try
            {
                await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                return true;
            }
            catch (DbUpdateException) when (attempt < MaxAttempts)
            {
                // Another process created it first, with its key ring. Read that one.
                database.ChangeTracker.Clear();
            }
        }
    }
}
