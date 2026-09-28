// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Nodes;
using DDT.Contracts.Settings;
using DDT.Core.Configuration;
using Microsoft.Extensions.Configuration;

namespace DDT.Server.Settings;

// A section of the settings page. Its options are the option classes configuration binds, so their validators check
// the page too.
public abstract class SettingsSectionDefinition
{
    private readonly Dictionary<string, SettingField> _byName;

    protected SettingsSectionDefinition(string name, string configurationPath, SettingsSectionKind kind, IReadOnlyList<SettingField> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        Name = name;
        ConfigurationPath = configurationPath;
        Kind = kind;
        Fields = fields;
        _byName = fields.ToDictionary(field => field.Name, StringComparer.Ordinal);
    }

    public string Name { get; }

    public string ConfigurationPath { get; }

    public SettingsSectionKind Kind { get; }

    public IReadOnlyList<SettingField> Fields { get; }

    public IEnumerable<SettingField> Secrets => Fields.Where(candidate => candidate.IsSecret);

    public SettingField? Field(string name) => _byName.GetValueOrDefault(name);

    public string ConfigurationKey(SettingField field)
    {
        ArgumentNullException.ThrowIfNull(field);

        return $"{ConfigurationPath}:{field.Path}";
    }

    public static string EnvironmentVariable(string configurationKey)
    {
        ArgumentNullException.ThrowIfNull(configurationKey);

        return configurationKey.Replace(":", "__", StringComparison.Ordinal);
    }

    // An empty value counts too, so configuration can force the safe value, such as no zero touch networks at all.
    public bool IsConfigured(IConfiguration configuration, SettingField field)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return configuration.GetSection(ConfigurationKey(field)).Exists();
    }

    // Configuration sets the field, and it is not one that configuration only seeds, so the page cannot change it.
    public bool IsLocked(IConfiguration configuration, SettingField field)
    {
        ArgumentNullException.ThrowIfNull(field);

        return !field.Seeds && IsConfigured(configuration, field);
    }

    // The longest field a problem's path starts with: BootTargets:X64Uefi:Method belongs to BootTargets.
    public SettingField? FieldOf(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return Fields
            .Where(field => path.Equals(field.Path, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(field.Path + ":", StringComparison.OrdinalIgnoreCase))
            .MaxBy(field => field.Path.Length);
    }

    // A map entry's key is free text, so the page names it in brackets: BootTargets:X64Uefi:Method becomes
    // bootTargets[X64Uefi].method. A key may hold colons, such as urn:example:admins, so only an entry member ends it.
    public string PageName(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (path.Length == 0)
        {
            return string.Empty;
        }

        SettingField? field = FieldOf(path);

        if (field is null)
        {
            return string.Join('.', path.Split(':').Select(SettingField.Camel));
        }

        string rest = path[field.Path.Length..].TrimStart(':');

        if (rest.Length == 0)
        {
            return field.Name;
        }

        if (field.Kind != SettingFieldKind.Collection)
        {
            return field.Name + "." + string.Join('.', rest.Split(':').Select(SettingField.Camel));
        }

        int separator = rest.LastIndexOf(':');

        return separator > 0 && field.EntryMembers.Contains(rest[(separator + 1)..], StringComparer.OrdinalIgnoreCase)
            ? $"{field.Name}[{rest[..separator]}].{SettingField.Camel(rest[(separator + 1)..])}"
            : $"{field.Name}[{rest}]";
    }

    // The code defaults, secrets left out.
    public abstract JsonObject Defaults();

    // The section as configuration alone sets it over the code defaults, secrets left out. A collection holds only its
    // configured entries. Throws InvalidOperationException for a value that cannot be converted.
    public abstract JsonObject Configured(IConfiguration configuration);

    // The options that values and secrets describe; secrets are keyed by field name.
    public abstract object Read(JsonObject values, IReadOnlyDictionary<string, string?> secrets);

    public abstract JsonObject Write(object options);

    public abstract string? SecretOf(object options, SettingField field);

    public abstract IReadOnlyList<SettingProblem> FindProblems(object options, SettingsContext context);

    // Current is what applies before a save, for the warnings about a change; null outside a save.
    public abstract IReadOnlyList<SettingWarning> FindWarnings(object options, object? current, SettingsContext context);
}
