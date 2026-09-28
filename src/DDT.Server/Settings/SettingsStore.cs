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

namespace DDT.Server.Settings;

// Reads and writes ddt."SettingsSections". A save writes the row and its audit rows in one SaveChanges and then
// publishes. This process applies it right away, and other processes read it at their next poll.
public sealed class SettingsStore(
    DdtDbContext database,
    SettingsProtector protector,
    SettingsSaveChecks checks,
    SettingsSecretChanges secretChanges,
    DdtSettings settings,
    TimeProvider timeProvider)
{
    // The shape of Values. A newer shape comes with an upgrade step that runs on load, so a rename happens in code.
    public const int SchemaVersion = 1;

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

    public async Task<SettingsSaveResult> SaveAsync(
        SettingsSectionDefinition definition,
        SettingsUpdate update,
        Actor actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(update);
        ArgumentNullException.ThrowIfNull(actor);

        if (!settings.KeyRingReadable)
        {
            return new(SettingsSaveOutcome.KeyRingUnreadable);
        }

        (SettingsSection? row, StoredSettingsSection current) = await ReadRowAsync(definition, cancellationToken).ConfigureAwait(false);

        if (update.Version != current.Version)
        {
            return new(SettingsSaveOutcome.Conflict);
        }

        SettingsSectionState before = settings.Preview(current, null)[definition.Name];
        DateTimeOffset now = timeProvider.GetUtcNow();
        JsonObject values = EditableValues(definition, before, current, update);
        (Dictionary<string, StoredSecret> secrets, List<string> changedSecrets, List<SettingProblem> secretProblems, List<SettingField> kept) =
            secretChanges.Apply(definition, before, current, update, now);

        StoredSettingsSection candidate = current with { Values = values, Secrets = secrets };
        SettingsSnapshot preview = settings.Preview(candidate, definition.Name);
        PendingSave save = new(definition, update, before, preview[definition.Name], preview);

        if (SettingsSecretChanges.Moved(definition, kept, before, save.After) is { Count: > 0 } moved)
        {
            return await RefuseMovedSecretsAsync(definition, moved, actor, now, cancellationToken).ConfigureAwait(false);
        }

        if (await GateAsync(save, secretProblems, actor, cancellationToken).ConfigureAwait(false) is { } refused)
        {
            return refused;
        }

        List<string> changes = [.. SettingsAudit.Changes(definition, before.StoredValues, save.After.StoredValues), .. changedSecrets];

        // Saving what's already stored changes nothing and records nothing.
        if (changes.Count == 0)
        {
            return new(SettingsSaveOutcome.Saved, settings.Current);
        }

        return await WriteAsync(new SettingsWrite(definition, row, candidate, actor, AuditActions.SettingsChanged, $"Changed {definition.Name}:", changes, now), cancellationToken)
            .ConfigureAwait(false);
    }

    // Writes the section unchanged with a new version, so every process applies it again. For PXE, that means each host
    // rescans its interfaces.
    public async Task<SettingsSaveResult> TouchAsync(SettingsSectionDefinition definition, Actor actor, string reason, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (!settings.KeyRingReadable)
        {
            return new(SettingsSaveOutcome.KeyRingUnreadable);
        }

        (SettingsSection? row, StoredSettingsSection current) = await ReadRowAsync(definition, cancellationToken).ConfigureAwait(false);

        return await WriteAsync(new SettingsWrite(definition, row, current, actor, AuditActions.SettingsChanged, reason, [], timeProvider.GetUtcNow()), cancellationToken)
            .ConfigureAwait(false);
    }

    // Used by the settings console. Every field takes its code default and every secret is cleared. The reset is
    // written, so a configured value isn't imported again at the next start. It isn't published, so the servers read
    // it at their next poll.
    public async Task ResetAsync(SettingsSectionDefinition definition, Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);

        (SettingsSection? row, StoredSettingsSection current) = await ReadRowAsync(definition, cancellationToken).ConfigureAwait(false);
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
            new SettingsWrite(definition, row, reset, actor, AuditActions.SettingsReset, $"Reset {definition.Name} to the defaults and cleared its secrets.", [], now)
            {
                Publish = false,
            },
            cancellationToken).ConfigureAwait(false);
    }

    // The row is tracked so WriteAsync can update it. It's null when the section was never written.
    internal async Task<(SettingsSection? Row, StoredSettingsSection Current)> ReadRowAsync(SettingsSectionDefinition definition, CancellationToken cancellationToken)
    {
        SettingsSection? row = await database.SettingsSections.FirstOrDefaultAsync(s => s.Section == definition.Name, cancellationToken).ConfigureAwait(false);

        return (row, row is null ? StoredSettingsSection.Empty(definition.Name) : protector.Decode(row));
    }

    // Returns Conflict when another process wrote the row since it was read. What this save staged is thrown away.
    internal async Task<SettingsSaveResult> WriteAsync(SettingsWrite write, CancellationToken cancellationToken)
    {
        SettingsSection row = write.Row ?? new SettingsSection { Section = write.Definition.Name };

        if (write.Row is null)
        {
            database.SettingsSections.Add(row);
        }

        SettingsProtector.Encode(write.Section, row);
        row.SchemaVersion = SchemaVersion;
        row.Version = write.Section.Version + 1;
        row.UpdatedUtc = write.Now;
        row.UpdatedByUserId = write.Actor.UserId;
        row.UpdatedByName = StoredText.Bound(write.Actor.Name, 256);
        database.AuditEvents.AddRange(SettingsAudit.Details(write.Lead, write.Changes)
            .Select(detail => AuditEvents.Create(write.Action, write.Definition.Name, write.Actor, write.Now, detail)));

        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            database.ChangeTracker.Clear();

            return new(SettingsSaveOutcome.Conflict);
        }

        StoredSettingsSection saved = write.Section with
        {
            SchemaVersion = SchemaVersion,
            Version = row.Version,
            UpdatedUtc = write.Now,
            UpdatedByUserId = write.Actor.UserId,
            UpdatedByName = row.UpdatedByName,
        };

        return new(SettingsSaveOutcome.Saved, write.Publish ? settings.Publish([saved]) : null);
    }

    // A locked field keeps its stored value. Configuration decides it, and the page's value applies again once the key
    // is gone.
    private static JsonObject EditableValues(
        SettingsSectionDefinition definition,
        SettingsSectionState before,
        StoredSettingsSection current,
        SettingsUpdate update)
    {
        JsonObject values = current.Values.DeepClone().AsObject();

        foreach (SettingField field in definition.Fields.Where(field => !field.IsSecret && !before.IsLocked(field)))
        {
            SettingsJson.Set(values, field, SettingsJson.Get(update.Values, field));
        }

        return values;
    }

    // A stored secret only goes to the server it was entered for. Otherwise an administrator could have it sent to
    // their own server. The attempt is audited.
    private async Task<SettingsSaveResult> RefuseMovedSecretsAsync(
        SettingsSectionDefinition definition,
        List<SettingProblem> moved,
        Actor actor,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        database.ChangeTracker.Clear();
        database.AuditEvents.AddRange(SettingsAudit.Details(
                $"Refused a save of {definition.Name} that kept a stored secret for a new server:",
                [.. moved.Select(problem => definition.PageName(problem.Field))])
            .Select(detail => AuditEvents.Create(AuditActions.SettingsRefused, definition.Name, actor, now, detail)));
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new(SettingsSaveOutcome.Invalid, Problems: Messages(definition, moved));
    }

    // Checks, in this order, the save's problems, the warnings it raises that nobody confirmed, and the fields that
    // need a fresh proof of identity. Returns null when none of them stops the save.
    private async Task<SettingsSaveResult?> GateAsync(
        PendingSave save,
        List<SettingProblem> secretProblems,
        Actor actor,
        CancellationToken cancellationToken)
    {
        SettingsSectionDefinition definition = save.Definition;
        SettingsSaveCheck check = await checks
            .CheckAsync(definition, new SettingsSectionChange(save.Before, save.After), actor, save.Update, cancellationToken)
            .ConfigureAwait(false);
        List<SettingProblem> problems = [.. secretProblems, .. save.After.Problems, .. check.Problems];

        if (problems.Count > 0)
        {
            return new(SettingsSaveOutcome.Invalid, Problems: Messages(definition, problems));
        }

        List<SettingWarning> unconfirmed = [.. Raised(save, check).Where(warning => !save.Update.Confirmed.Contains(warning.Code!))];

        if (unconfirmed.Count > 0)
        {
            return new(
                SettingsSaveOutcome.Invalid,
                Unconfirmed: [.. unconfirmed.Select(warning => new SettingMessage(definition.PageName(warning.Field), warning.Message, warning.Code, warning.Text))]);
        }

        List<string> reauthenticate = [.. definition.Fields
            .Where(field => field.Reauthenticate
                && !save.Before.IsLocked(field)
                && !SettingsJson.Same(SettingsJson.Get(save.Before.Values, field), SettingsJson.Get(save.After.Values, field)))
            .Select(field => field.Name)];

        return reauthenticate.Count > 0 && !save.Update.Reauthenticated
            ? new(SettingsSaveOutcome.Reauthenticate, Fields: reauthenticate)
            : null;
    }

    // A warning needs a confirmation when the save raises it. That means it didn't hold before, or it's about the
    // change itself. A warning about the accounts, not the values, is raised at every save while it holds.
    private static IEnumerable<SettingWarning> Raised(PendingSave save, SettingsSaveCheck check)
    {
        SettingsContext context = new()
        {
            Machines = (MachineOptions)save.Preview[SettingsSectionNames.Machines].Options,
            Proxies = (DdtForwardedHeadersOptions)save.Preview[SettingsSectionNames.Proxies].Options,
            HttpBootPort = ((PxeOptions)save.Preview[SettingsSectionNames.Pxe].Options).HttpBootPort,
            Saving = save.Definition.Name,
        };

        IEnumerable<SettingWarning> raised = save.Definition.FindWarnings(save.After.Options, save.Before.Options, context)
            .Where(warning => !save.Before.Warnings.Contains(warning));

        return raised
            .Concat(check.Warnings)
            .Where(warning => warning.Code is { } code && SettingWarningCodes.NeedConfirmation.Contains(code))
            .Distinct();
    }

    private static List<SettingMessage> Messages(SettingsSectionDefinition definition, IEnumerable<SettingProblem> problems) =>
        [.. problems.Select(problem => new SettingMessage(definition.PageName(problem.Field), problem.Message, null, problem.Text))];

    // A save between reading the section and writing it. After is the section as the save would leave it.
    private sealed record PendingSave(
        SettingsSectionDefinition Definition,
        SettingsUpdate Update,
        SettingsSectionState Before,
        SettingsSectionState After,
        SettingsSnapshot Preview);
}
