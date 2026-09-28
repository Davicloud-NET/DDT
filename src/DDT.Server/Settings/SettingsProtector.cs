// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using DDT.Server.Data;
using Microsoft.AspNetCore.DataProtection;

namespace DDT.Server.Settings;

// Encrypts secrets with the key ring of cookies and machine tokens, for a purpose that names section and field, so a
// ciphertext copied elsewhere does not decrypt. That protects a copy of the database, such as a backup, not the store
// volume, which holds the key ring.
public sealed class SettingsProtector(IDataProtectionProvider provider)
{
    private const string Purpose = "DDT.Settings";
    private const string CanaryField = "canary";
    private const string CanaryValue = "DDT settings key ring";

    public string Protect(string section, SettingField field, string value)
    {
        ArgumentNullException.ThrowIfNull(field);

        return provider.CreateProtector(Purpose, section, field.Path).Protect(value);
    }

    public string? Unprotect(string section, SettingField field, string protectedValue)
    {
        ArgumentNullException.ThrowIfNull(field);

        try
        {
            return provider.CreateProtector(Purpose, section, field.Path).Unprotect(protectedValue);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    // The reserved keyring row holds a known value, which a process whose key ring differs cannot read.
    public string ProtectCanary() => provider.CreateProtector(Purpose, SettingsSectionNames.KeyRing, CanaryField).Protect(CanaryValue);

    public bool ReadsCanary(SettingsSection row)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (Documents(row.Secrets).GetValueOrDefault(CanaryField)?.Protected is not { } canary)
        {
            return false;
        }

        try
        {
            return provider.CreateProtector(Purpose, SettingsSectionNames.KeyRing, CanaryField).Unprotect(canary) == CanaryValue;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    public string CanarySecrets(DateTimeOffset now) =>
        JsonSerializer.Serialize(
            new Dictionary<string, StoredSecretDocument> { [CanaryField] = new(ProtectCanary(), now) },
            SettingsJsonContext.Default.DictionaryStringStoredSecretDocument);

    // A value of another build that no longer parses reads as nothing written, and the fields take their defaults.
    public StoredSettingsSection Decode(SettingsSection row)
    {
        ArgumentNullException.ThrowIfNull(row);

        JsonObject values;

        try
        {
            values = JsonNode.Parse(row.Values) as JsonObject ?? [];
        }
        catch (JsonException)
        {
            values = [];
        }

        SettingsSectionDefinition? definition = SettingsDefinitions.Find(row.Section);
        Dictionary<string, StoredSecret> secrets = new(StringComparer.Ordinal);

        foreach ((string name, StoredSecretDocument document) in Documents(row.Secrets))
        {
            if (document.Protected is null)
            {
                secrets[name] = new StoredSecret(null, false, document.UpdatedUtc, null);
                continue;
            }

            // A secret this build does not know is kept as it is, unread.
            string? value = definition?.Field(name) is { IsSecret: true } field ? Unprotect(row.Section, field, document.Protected) : null;
            secrets[name] = new StoredSecret(value, value is null, document.UpdatedUtc, document.Protected);
        }

        return new StoredSettingsSection(
            row.Section,
            row.SchemaVersion,
            values,
            secrets,
            row.Version,
            row.UpdatedUtc,
            row.UpdatedByUserId,
            row.UpdatedByName);
    }

    public static void Encode(StoredSettingsSection section, SettingsSection row)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(row);

        row.Values = section.Values.ToJsonString();
        row.Secrets = JsonSerializer.Serialize(
            section.Secrets.ToDictionary(secret => secret.Key, secret => new StoredSecretDocument(secret.Value.Protected, secret.Value.UpdatedUtc), StringComparer.Ordinal),
            SettingsJsonContext.Default.DictionaryStringStoredSecretDocument);
    }

    private static Dictionary<string, StoredSecretDocument> Documents(string secrets)
    {
        try
        {
            return JsonSerializer.Deserialize(secrets, SettingsJsonContext.Default.DictionaryStringStoredSecretDocument) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
