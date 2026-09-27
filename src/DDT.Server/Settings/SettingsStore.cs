// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Nodes;
using DDT.Contracts.Settings;
using DDT.Core.Configuration;
using DDT.Pxe;
using DDT.Server.Data;
using DDT.Server.Machines;
using DDT.Server.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DDT.Server.Settings;

// Reads and writes ddt."SettingsSections". A save writes the row and its audit rows in one SaveChanges, as the other
// stores of DDT do, and then publishes, so the process that saved applies the change at once. Other processes on the
// same database read it at their next poll.
public sealed partial class SettingsStore(
    DdtDbContext database,
    SettingsProtector protector,
    SettingsSaveChecks checks,
    DdtSettings settings,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<SettingsStore> logger)
{
    // The shape of Values. A newer one comes with an upgrade step that runs on load, so a rename happens in code.
    public const int SchemaVersion = 1;

    private const int MaxImportAttempts = 5;

    public async Task<IReadOnlyList<StoredSettingsSection>> LoadAsync(CancellationToken cancellationToken)
    {
        List<SettingsSection> rows = await database.SettingsSections.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);

        return [.. rows.Where(row => SettingsDefinitions.Find(row.Section) is not null).Select(protector.Decode)];
    }

    public async Task<IReadOnlyList<StoredSettingsSection>> LoadAsync(IReadOnlyCollection<string> sections, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sections);

        List<SettingsSection> rows = await database.SettingsSections
            .AsNoTracking()
            .Where(row => sections.Contains(row.Section))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. rows.Where(row => SettingsDefinitions.Find(row.Section) is not null).Select(protector.Decode)];
    }

    public Task<Dictionary<string, long>> VersionsAsync(CancellationToken cancellationToken) =>
        database.SettingsSections.AsNoTracking().ToDictionaryAsync(row => row.Section, row => row.Version, cancellationToken);

    // At every start, before the first publish. A field never written takes its configured value, if configuration has
    // its key; code defaults are never imported. So a process that starts first cannot freeze its defaults over another
    // process's configured values. A process whose key ring cannot read the stored secrets imports nothing at all.
    public async Task<bool> ImportAsync(CancellationToken cancellationToken)
    {
        bool readable = await CheckKeyRingAsync(cancellationToken).ConfigureAwait(false);
        settings.KeyRingReadable = readable;

        if (!readable)
        {
            LogKeyRingUnreadable();

            return false;
        }

        foreach (SettingsSectionDefinition definition in SettingsDefinitions.All)
        {
            // Two processes that create the same row conflict on its key, and two that import into it on its version.
            // The loser reads again and imports what is still missing.
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    await ImportAsync(definition, cancellationToken).ConfigureAwait(false);

                    break;
                }
                catch (DbUpdateException) when (attempt < MaxImportAttempts)
                {
                    database.ChangeTracker.Clear();
                }
            }
        }

        return true;
    }

    public async Task<SettingsSaveResult> SaveAsync(
        SettingsSectionDefinition definition,
        SettingsUpdate update,
        SettingsActor actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(update);
        ArgumentNullException.ThrowIfNull(actor);

        if (!settings.KeyRingReadable)
        {
            return new(SettingsSaveOutcome.KeyRingUnreadable);
        }

        SettingsSection? row = await database.SettingsSections.FirstOrDefaultAsync(s => s.Section == definition.Name, cancellationToken).ConfigureAwait(false);
        StoredSettingsSection current = row is null ? StoredSettingsSection.Empty(definition.Name) : protector.Decode(row);

        if (update.Version != current.Version)
        {
            return new(SettingsSaveOutcome.Conflict);
        }

        SettingsSectionState before = settings.Preview(current, null)[definition.Name];
        DateTimeOffset now = timeProvider.GetUtcNow();
        JsonObject values = current.Values.DeepClone().AsObject();

        // A locked field keeps its stored value: configuration decides it, and the page's value applies again once the key
        // is gone.
        foreach (SettingField field in definition.Fields.Where(field => !field.IsSecret && !before.IsLocked(field)))
        {
            SettingsJson.Set(values, field, SettingsJson.Get(update.Values, field));
        }

        (Dictionary<string, StoredSecret> secrets, List<string> secretChanges, List<SettingProblem> secretProblems, List<SettingField> kept) =
            Secrets(definition, before, current, update, now);

        StoredSettingsSection candidate = current with { Values = values, Secrets = secrets };
        SettingsSnapshot preview = settings.Preview(candidate, definition.Name);
        SettingsSectionState after = preview[definition.Name];

        // A stored secret goes only to the server it was entered for, or an administrator could have it sent to their own.
        List<SettingProblem> refused = [.. kept.Where(field => DestinationChanged(definition, field, before, after))
            .Select(field => new SettingProblem(field.Path, "Enter it again for the new server: a stored secret goes only to the server it was entered for."))];

        if (refused.Count > 0)
        {
            database.ChangeTracker.Clear();
            database.AuditEvents.AddRange(SettingsAudit.Rows(
                AuditActions.SettingsRefused,
                definition.Name,
                actor,
                now,
                $"Refused a save of {definition.Name} that kept a stored secret for a new server:",
                [.. refused.Select(problem => definition.PageName(problem.Field))]));
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return new(SettingsSaveOutcome.Invalid, Problems: Messages(definition, refused));
        }

        SettingsSaveCheck check = await checks.CheckAsync(definition, before, after, actor, update, cancellationToken).ConfigureAwait(false);
        List<SettingProblem> problems = [.. secretProblems, .. after.Problems, .. check.Problems];

        if (problems.Count > 0)
        {
            return new(SettingsSaveOutcome.Invalid, Problems: Messages(definition, problems));
        }

        List<SettingWarning> unconfirmed = [.. Raised(definition, before, after, preview, check)
            .Where(warning => !update.Confirmed.Contains(warning.Code!))];

        if (unconfirmed.Count > 0)
        {
            return new(
                SettingsSaveOutcome.Invalid,
                Unconfirmed: [.. unconfirmed.Select(warning => new SettingMessage(definition.PageName(warning.Field), warning.Message, warning.Code))]);
        }

        List<string> reauthenticate = [.. definition.Fields
            .Where(field => field.Reauthenticate
                && !before.IsLocked(field)
                && !SettingsJson.Same(SettingsJson.Get(before.Values, field), SettingsJson.Get(after.Values, field)))
            .Select(field => field.Name)];

        if (reauthenticate.Count > 0 && !update.Reauthenticated)
        {
            return new(SettingsSaveOutcome.Reauthenticate, Fields: reauthenticate);
        }

        List<string> changes = [.. SettingsAudit.Changes(definition, before.StoredValues, after.StoredValues), .. secretChanges];

        // A save of what is stored already changes nothing and records nothing.
        if (changes.Count == 0)
        {
            return new(SettingsSaveOutcome.Saved, settings.Current);
        }

        return await WriteAsync(definition, row, candidate, actor, AuditActions.SettingsChanged, $"Changed {definition.Name}:", changes, now, cancellationToken)
            .ConfigureAwait(false);
    }

    // The section as it is, with a new version, so that every process applies it again: for pxe, it scans its
    // interfaces anew.
    public async Task<SettingsSaveResult> TouchAsync(SettingsSectionDefinition definition, SettingsActor actor, string reason, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (!settings.KeyRingReadable)
        {
            return new(SettingsSaveOutcome.KeyRingUnreadable);
        }

        SettingsSection? row = await database.SettingsSections.FirstOrDefaultAsync(s => s.Section == definition.Name, cancellationToken).ConfigureAwait(false);
        StoredSettingsSection current = row is null ? StoredSettingsSection.Empty(definition.Name) : protector.Decode(row);

        return await WriteAsync(definition, row, current, actor, AuditActions.SettingsChanged, reason, [], timeProvider.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false);
    }

    // For the console: every field takes its code default, and every secret is cleared. Written rather than left
    // unwritten, so a configured value is not imported again at the next start. It does not publish: the servers read
    // it at their next poll.
    public async Task ResetAsync(SettingsSectionDefinition definition, SettingsActor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);

        SettingsSection? row = await database.SettingsSections.FirstOrDefaultAsync(s => s.Section == definition.Name, cancellationToken).ConfigureAwait(false);
        StoredSettingsSection current = row is null ? StoredSettingsSection.Empty(definition.Name) : protector.Decode(row);
        DateTimeOffset now = timeProvider.GetUtcNow();
        JsonObject defaults = definition.Defaults();
        JsonObject values = [];

        foreach (SettingField field in definition.Fields.Where(field => !field.IsSecret))
        {
            SettingsJson.Set(values, field, SettingsJson.Get(defaults, field));
        }

        Dictionary<string, StoredSecret> secrets = definition.Secrets.ToDictionary(
            field => field.Name,
            _ => new StoredSecret(null, false, now, null),
            StringComparer.Ordinal);

        StoredSettingsSection reset = current with { Values = values, Secrets = secrets };

        await WriteAsync(
            definition,
            row,
            reset,
            actor,
            AuditActions.SettingsReset,
            $"Reset {definition.Name} to the defaults and cleared its secrets.",
            [],
            now,
            cancellationToken,
            publish: false).ConfigureAwait(false);
    }

    private async Task<SettingsSaveResult> WriteAsync(
        SettingsSectionDefinition definition,
        SettingsSection? row,
        StoredSettingsSection section,
        SettingsActor actor,
        string action,
        string lead,
        IReadOnlyList<string> changes,
        DateTimeOffset now,
        CancellationToken cancellationToken,
        bool publish = true)
    {
        if (row is null)
        {
            row = new SettingsSection { Section = definition.Name };
            database.SettingsSections.Add(row);
        }

        SettingsProtector.Encode(section, row);
        row.SchemaVersion = SchemaVersion;
        row.Version = section.Version + 1;
        row.UpdatedUtc = now;
        row.UpdatedByUserId = actor.UserId;
        row.UpdatedByName = StoredText.Bound(actor.Name, 256);
        database.AuditEvents.AddRange(SettingsAudit.Rows(action, definition.Name, actor, now, lead, changes));

        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            database.ChangeTracker.Clear();

            return new(SettingsSaveOutcome.Conflict);
        }

        StoredSettingsSection saved = section with
        {
            SchemaVersion = SchemaVersion,
            Version = row.Version,
            UpdatedUtc = now,
            UpdatedByUserId = actor.UserId,
            UpdatedByName = row.UpdatedByName,
        };

        return new(SettingsSaveOutcome.Saved, publish ? settings.Publish([saved]) : null);
    }

    private (Dictionary<string, StoredSecret> Secrets, List<string> Changes, List<SettingProblem> Problems, List<SettingField> Kept) Secrets(
        SettingsSectionDefinition definition,
        SettingsSectionState before,
        StoredSettingsSection current,
        SettingsUpdate update,
        DateTimeOffset now)
    {
        Dictionary<string, StoredSecret> secrets = new(current.Secrets, StringComparer.Ordinal);
        List<string> changes = [];
        List<SettingProblem> problems = [];
        List<SettingField> kept = [];

        foreach (SettingField field in definition.Secrets.Where(field => !before.IsLocked(field)))
        {
            SecretUpdate change = update.Secrets.GetValueOrDefault(field.Name) ?? new SecretUpdate(SecretAction.Keep, null);
            StoredSecret? held = current.Secrets.GetValueOrDefault(field.Name);

            switch (change.Action)
            {
                case SecretAction.Set when !string.IsNullOrEmpty(change.Value):
                    secrets[field.Name] = new StoredSecret(change.Value, false, now, protector.Protect(definition.Name, field, change.Value));
                    changes.Add($"{field.Name} set");
                    break;

                case SecretAction.Set or SecretAction.Clear:
                    if (held?.Value is not null || held?.Unreadable == true)
                    {
                        secrets[field.Name] = new StoredSecret(null, false, now, null);
                        changes.Add($"{field.Name} cleared");
                    }

                    break;

                default:
                    if (held?.Unreadable == true)
                    {
                        problems.Add(new(field.Path, "No longer decrypts with this server's key ring, so it cannot be kept. Enter it again or clear it."));
                    }
                    else if (!string.IsNullOrEmpty(held?.Value))
                    {
                        kept.Add(field);
                    }

                    break;
            }
        }

        return (secrets, changes, problems, kept);
    }

    private static bool DestinationChanged(SettingsSectionDefinition definition, SettingField field, SettingsSectionState before, SettingsSectionState after)
    {
        IReadOnlyList<string> destination = (definition.Name, field.Path) switch
        {
            (SettingsSectionNames.Ldap, "BindPassword") => LdapSettingsSection.Destination,
            (SettingsSectionNames.Oidc, "ClientSecret") => OidcSettingsSection.Destination,
            _ => [],
        };

        return destination
            .Select(path => definition.FieldOf(path)!)
            .Any(target => !SettingsJson.Same(SettingsJson.Get(before.Values, target), SettingsJson.Get(after.Values, target)));
    }

    // A warning needs a confirmation when the save raises it: it did not hold before, or it is about the change itself.
    // One about the accounts, not the values, holds at every save while it holds at all.
    private static IEnumerable<SettingWarning> Raised(
        SettingsSectionDefinition definition,
        SettingsSectionState before,
        SettingsSectionState after,
        SettingsSnapshot preview,
        SettingsSaveCheck check)
    {
        SettingsContext context = new()
        {
            Machines = (MachineOptions)preview[SettingsSectionNames.Machines].Options,
            Proxies = (DdtForwardedHeadersOptions)preview[SettingsSectionNames.Proxies].Options,
            HttpBootPort = ((PxeOptions)preview[SettingsSectionNames.Pxe].Options).HttpBootPort,
            Saving = definition.Name,
        };

        IEnumerable<SettingWarning> raised = definition.FindWarnings(after.Options, before.Options, context)
            .Where(warning => !before.Warnings.Contains(warning));

        return raised
            .Concat(check.Warnings)
            .Where(warning => warning.Code is { } code && SettingWarningCodes.NeedConfirmation.Contains(code))
            .Distinct();
    }

    private static List<SettingMessage> Messages(SettingsSectionDefinition definition, IEnumerable<SettingProblem> problems) =>
        [.. problems.Select(problem => new SettingMessage(definition.PageName(problem.Field), problem.Message, null))];

    private async Task ImportAsync(SettingsSectionDefinition definition, CancellationToken cancellationToken)
    {
        JsonObject configured;

        // A value that cannot be converted stopped the start already, in the configuration check.
        try
        {
            configured = definition.Configured(configuration);
        }
        catch (InvalidOperationException)
        {
            return;
        }

        SettingsSection? row = await database.SettingsSections.FirstOrDefaultAsync(s => s.Section == definition.Name, cancellationToken).ConfigureAwait(false);
        StoredSettingsSection current = row is null ? StoredSettingsSection.Empty(definition.Name) : protector.Decode(row);
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

        SettingsSaveResult written = await WriteAsync(
            definition,
            row,
            current with { Values = values, Secrets = secrets },
            SettingsActor.Configuration,
            AuditActions.SettingsImported,
            $"Imported into {definition.Name} from configuration:",
            imported,
            now,
            cancellationToken,
            publish: false).ConfigureAwait(false);

        if (written.Outcome == SettingsSaveOutcome.Conflict)
        {
            throw new DbUpdateException($"{definition.Name} changed while its configured values were imported.");
        }

        LogImported(definition.Name, string.Join(", ", imported));
    }

    private async Task<bool> CheckKeyRingAsync(CancellationToken cancellationToken)
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
                SchemaVersion = SchemaVersion,
                Secrets = protector.CanarySecrets(now),
                Version = 1,
                UpdatedUtc = now,
                UpdatedByName = SettingsActor.Configuration.Name,
            });

            try
            {
                await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                return true;
            }
            catch (DbUpdateException) when (attempt < MaxImportAttempts)
            {
                // Another process created it first, with its key ring. Read that one.
                database.ChangeTracker.Clear();
            }
        }
    }

    [LoggerMessage(EventId = 950, Level = LogLevel.Information, Message = "Imported the configured {Section} settings {Fields}")]
    private partial void LogImported(string section, string fields);

    [LoggerMessage(
        EventId = 951,
        Level = LogLevel.Warning,
        Message = "This server's key ring cannot read the stored settings secrets, so it saves no settings. Every DDT process on one database has to share the key ring in DDT:StorePath/keys.")]
    private partial void LogKeyRingUnreadable();
}
