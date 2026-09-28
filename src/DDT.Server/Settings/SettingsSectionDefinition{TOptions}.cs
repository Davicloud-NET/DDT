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

public abstract class SettingsSectionDefinition<TOptions> : SettingsSectionDefinition
    where TOptions : class, new()
{
    protected SettingsSectionDefinition(string name, string configurationPath, SettingsSectionKind kind, IReadOnlyList<SettingField> fields)
        : base(name, configurationPath, kind, fields)
    {
    }

    protected abstract JsonTypeInfo<TOptions> TypeInfo { get; }

    // Each section binds with its concrete type, because the binding generator can't bind a type parameter.
    protected abstract TOptions? Bind(IConfigurationSection section);

    protected virtual JsonNode? BindCollection(IConfigurationSection section, SettingField field) =>
        throw new InvalidOperationException($"{Name} has no collection {field.Name}.");

    protected virtual void SetSecret(TOptions options, SettingField field, string? value) =>
        throw new InvalidOperationException($"{Name} has no secret {field.Name}.");

    protected virtual string? GetSecret(TOptions options, SettingField field) =>
        throw new InvalidOperationException($"{Name} has no secret {field.Name}.");

    // Deserializing a collection loses the comparer its property starts with, so this puts it back.
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

        // The binder adds configured entries to a collection's defaults, so each collection is bound again by itself.
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
