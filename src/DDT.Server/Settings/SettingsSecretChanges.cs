// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;
using DDT.Contracts.Settings;
using DDT.Core.Configuration;

namespace DDT.Server.Settings;

// What a save does to a section's secrets. A secret configuration locks is left alone.
public sealed class SettingsSecretChanges(SettingsProtector protector)
{
    // Kept lists the stored secrets the save keeps, which may go only where they went before.
    public (Dictionary<string, StoredSecret> Secrets, List<string> Changes, List<SettingProblem> Problems, List<SettingField> Kept) Apply(
        SettingsSectionDefinition definition,
        SettingsSectionState before,
        StoredSettingsSection current,
        SettingsUpdate update,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(update);

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
                        problems.Add(new(field.Path, ServerMessages.SettingsSecretCannotBeKept.With()));
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

    // The kept secrets whose destination the save changes, as problems of their fields.
    public static List<SettingProblem> Moved(
        SettingsSectionDefinition definition,
        IEnumerable<SettingField> kept,
        SettingsSectionState before,
        SettingsSectionState after)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(kept);

        return [.. kept.Where(field => DestinationChanged(definition, field, before, after))
            .Select(field => new SettingProblem(field.Path, ServerMessages.SettingsSecretForNewServer.With()))];
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
}
