// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using DDT.Contracts.Settings;
using DDT.Core.Configuration;
using Microsoft.Extensions.Configuration;

namespace DDT.Server.Settings;

// A section of the settings page: its fields, its key in configuration, and how its options are read, written and
// checked. Options are the existing option classes, so the validators that checked configuration check the page too.
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

    // Present for any source and even with an empty value, so that configuration can force the safe value, such as no
    // zero touch networks at all.
    public bool IsConfigured(IConfiguration configuration, SettingField field)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return configuration.GetSection(ConfigurationKey(field)).Exists();
    }

    // Configuration decides the field: its key is present, and the field is not one configuration only seeds.
    public bool IsLocked(IConfiguration configuration, SettingField field)
    {
        ArgumentNullException.ThrowIfNull(field);

        return !field.Seeds && IsConfigured(configuration, field);
    }

    // The field a problem's path belongs to, the longest that matches: BootTargets:X64Uefi:Method belongs to BootTargets.
    public SettingField? FieldOf(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return Fields
            .Where(field => path.Equals(field.Path, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(field.Path + ":", StringComparison.OrdinalIgnoreCase))
            .MaxBy(field => field.Path.Length);
    }

    // A problem's path as the page names it. An entry of a map is named in brackets, because its key is free text:
    // BootTargets:X64Uefi:Method becomes bootTargets[X64Uefi].method.
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

        int separator = rest.IndexOf(':', StringComparison.Ordinal);

        return separator < 0
            ? $"{field.Name}[{rest}]"
            : $"{field.Name}[{rest[..separator]}]." + string.Join('.', rest[(separator + 1)..].Split(':').Select(SettingField.Camel));
    }

    // The code defaults, secrets left out.
    public abstract JsonObject Defaults();

    // The section as configuration alone sets it over the code defaults, secrets left out. A collection holds only its
    // configured entries. Throws InvalidOperationException for a value that cannot be converted.
    public abstract JsonObject Configured(IConfiguration configuration);

    // The options a document of values and the secrets, keyed by field name, describe.
    public abstract object Read(JsonObject values, IReadOnlyDictionary<string, string?> secrets);

    public abstract JsonObject Write(object options);

    public abstract string? SecretOf(object options, SettingField field);

    public abstract IReadOnlyList<SettingProblem> FindProblems(object options, SettingsContext context);

    // Current is what applies before a save, for the warnings about a change; null outside a save.
    public abstract IReadOnlyList<SettingWarning> FindWarnings(object options, object? current, SettingsContext context);
}

public abstract class SettingsSectionDefinition<TOptions> : SettingsSectionDefinition
    where TOptions : class, new()
{
    protected SettingsSectionDefinition(string name, string configurationPath, SettingsSectionKind kind, IReadOnlyList<SettingField> fields)
        : base(name, configurationPath, kind, fields)
    {
    }

    protected abstract JsonTypeInfo<TOptions> TypeInfo { get; }

    // With the concrete type at each call, because the binding generator cannot bind a type parameter.
    protected abstract TOptions? Bind(IConfigurationSection section);

    protected virtual JsonNode? BindCollection(IConfigurationSection section, SettingField field) =>
        throw new InvalidOperationException($"{Name} has no collection {field.Name}.");

    protected virtual void SetSecret(TOptions options, SettingField field, string? value) =>
        throw new InvalidOperationException($"{Name} has no secret {field.Name}.");

    protected virtual string? GetSecret(TOptions options, SettingField field) =>
        throw new InvalidOperationException($"{Name} has no secret {field.Name}.");

    // Deserializing a collection loses the comparer its property starts with, so it is put back here.
    protected virtual void Normalize(TOptions options)
    {
    }

    protected virtual IReadOnlyList<SettingProblem> FindProblems(TOptions options, SettingsContext context) => [];

    protected virtual IReadOnlyList<SettingWarning> FindWarnings(TOptions options, TOptions? current, SettingsContext context) => [];

    public override JsonObject Defaults() => Serialize(new TOptions());

    public override JsonObject Configured(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        IConfigurationSection section = configuration.GetSection(ConfigurationPath);
        JsonObject values = Serialize(Bind(section) ?? new TOptions());
        JsonObject defaults = Defaults();

        // The binder adds configured entries to a collection's defaults, so a collection is bound again on its own.
        foreach (SettingField field in Fields.Where(field => field.Kind == SettingFieldKind.Collection))
        {
            IConfigurationSection child = section.GetSection(field.Path);
            SettingsJson.Set(values, field, child.Exists() ? BindCollection(child, field) : SettingsJson.Get(defaults, field));
        }

        return values;
    }

    public override object Read(JsonObject values, IReadOnlyDictionary<string, string?> secrets) => ReadOptions(values, secrets);

    public TOptions ReadOptions(JsonObject values, IReadOnlyDictionary<string, string?> secrets)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(secrets);

        TOptions options = values.Deserialize(TypeInfo) ?? new TOptions();

        foreach (SettingField field in Secrets)
        {
            SetSecret(options, field, secrets.GetValueOrDefault(field.Name));
        }

        Normalize(options);

        return options;
    }

    public override JsonObject Write(object options) => Serialize((TOptions)options);

    public override string? SecretOf(object options, SettingField field) => GetSecret((TOptions)options, field);

    public override IReadOnlyList<SettingProblem> FindProblems(object options, SettingsContext context) =>
        FindProblems((TOptions)options, context);

    public override IReadOnlyList<SettingWarning> FindWarnings(object options, object? current, SettingsContext context) =>
        FindWarnings((TOptions)options, (TOptions?)current, context);

    private JsonObject Serialize(TOptions options) => JsonSerializer.SerializeToNode(options, TypeInfo)!.AsObject();
}
