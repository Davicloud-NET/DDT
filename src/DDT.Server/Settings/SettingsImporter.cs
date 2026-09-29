// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Nodes;
using DDT.Server.Data;
using DDT.Server.Machines;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DDT.Server.Settings;

// Imports configured values at every start, before the first publish. Only a field that was never written takes its
// configured value, and code defaults are never imported. That way a process that starts first can't freeze its
// defaults over another process's configured values. It logs under SettingsStore's category, which the logging
// settings may name.
public sealed partial class SettingsImporter(
    SettingsStore store,
    SettingsKeyRing keyRing,
    SettingsProtector protector,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<SettingsStore> logger)
{
    private const int MaxAttempts = 5;

    // A process whose key ring cannot read the stored secrets imports nothing at all.
    public async Task<bool> ImportAsync(CancellationToken cancellationToken)
    {
        if (!await keyRing.CheckAsync(cancellationToken).ConfigureAwait(false))
        {
            LogKeyRingUnreadable();

            return false;
        }

        foreach (SettingsSectionDefinition definition in SettingsDefinitions.All)
        {
            // Two processes that create the same row conflict on its key. Two that import into the same row conflict on
            // its version. The loser reads again and imports what's still missing.
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    await ImportAsync(definition, cancellationToken).ConfigureAwait(false);

                    break;
                }
                catch (DbUpdateException) when (attempt < MaxAttempts)
                {
                    // The write that failed already threw away what it staged.
                }
            }
        }

        return true;
    }

    private async Task ImportAsync(SettingsSectionDefinition definition, CancellationToken cancellationToken)
    {
        if (Configured(definition) is not { } configured)
        {
            return;
        }

        (SettingsSection? row, StoredSettingsSection current) = await store.ReadRowAsync(definition, cancellationToken).ConfigureAwait(false);
        DateTimeOffset now = timeProvider.GetUtcNow();
        JsonObject values = current.Values.DeepClone().AsObject();
        Dictionary<string, StoredSecret> secrets = new(current.Secrets, StringComparer.Ordinal);
        List<string> imported = [];

        foreach (SettingField field in definition.Fields.Where(field => definition.IsConfigured(configuration, field)))
        {
            if (field.IsSecret)
            {
                if (current.Secrets.ContainsKey(field.Name))
                {
                    continue;
                }

                string? value = configuration[definition.ConfigurationKey(field)];
                secrets[field.Name] = string.IsNullOrEmpty(value)
                    ? new StoredSecret(null, false, now, null)
                    : new StoredSecret(value, false, now, protector.Protect(definition.Name, field, value));
            }
            else
            {
                if (SettingsJson.Has(current.Values, field))
                {
                    continue;
                }

                SettingsJson.Set(values, field, SettingsJson.Get(configured, field));
            }

            imported.Add(field.Name);
        }

        if (imported.Count == 0)
        {
            return;
        }

        SettingsWrite write = new(
            definition,
            row,
            current with { Values = values, Secrets = secrets },
            Actor.Configuration,
            AuditActions.SettingsImported,
            $"Imported into {definition.Name} from configuration:",
            imported,
            now)
        {
            Publish = false,
        };

        await WriteAsync(write, cancellationToken).ConfigureAwait(false);
    }

    // Returns null when a value can't be converted. The configuration check already stopped the start in that case.
    private JsonObject? Configured(SettingsSectionDefinition definition)
    {
        try
        {
            return definition.Configured(configuration);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private async Task WriteAsync(SettingsWrite write, CancellationToken cancellationToken)
    {
        if ((await store.WriteAsync(write, cancellationToken).ConfigureAwait(false)).Outcome == SettingsSaveOutcome.Conflict)
        {
            throw new DbUpdateException($"{write.Definition.Name} changed while its configured values were imported.");
        }

        LogImported(write.Definition.Name, string.Join(", ", write.Changes));
    }

    [LoggerMessage(EventId = 950, Level = LogLevel.Information, Message = "Imported the configured {Section} settings {Fields}")]
    private partial void LogImported(string section, string fields);

    [LoggerMessage(
        EventId = 951,
        Level = LogLevel.Warning,
        Message = "This server's key ring cannot read the stored settings secrets, so it saves no settings. Every DDT process on one database has to share the key ring in DDT:StorePath/keys.")]
    private partial void LogKeyRingUnreadable();
}
