// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;
using System.Text.Json.Nodes;
using DDT.Contracts.Messages;
using DDT.Contracts.Settings;
using DDT.Core.Configuration;
using DDT.Pxe;
using DDT.Server.Authentication;
using DDT.Server.Deployments;
using DDT.Server.Ldap;
using DDT.Server.Machines;
using DDT.Server.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DDT.Server.Settings;

// The only place that builds a SettingsSnapshot. A field that configuration locks takes its configured value. Every
// other field, seeded ones included, takes its stored value or its default.
internal static class SettingsSnapshotBuilder
{
    public static SettingsSnapshot Build(IReadOnlyDictionary<string, StoredSettingsSection> stored, IConfiguration configuration, string? saving)
    {
        ArgumentNullException.ThrowIfNull(stored);
        ArgumentNullException.ThrowIfNull(configuration);

        SettingsBootstrap bootstrap = SettingsBootstrap.From(configuration);
        Dictionary<string, Draft> drafts = [];

        foreach (SettingsSectionDefinition definition in SettingsDefinitions.All)
        {
            Draft draft = Read(definition, stored.GetValueOrDefault(definition.Name), configuration);

            if (draft.Options is PxeOptions pxe)
            {
                pxe.HttpBootPort = bootstrap.HttpBootPort;
                pxe.BootDirectory = bootstrap.BootDirectory;
            }

            drafts[definition.Name] = draft;
        }

        SettingsContext context = new()
        {
            Machines = (MachineOptions)drafts[SettingsSectionNames.Machines].Options,
            Proxies = (DdtForwardedHeadersOptions)drafts[SettingsSectionNames.Proxies].Options,
            HttpBootPort = bootstrap.HttpBootPort,
            Saving = saving,
        };

        Dictionary<string, SettingsSectionState> sections = new(StringComparer.Ordinal);

        foreach ((string name, Draft draft) in drafts)
        {
            sections[name] = State(draft, context);
        }

        return new SettingsSnapshot(sections, InForce(sections, drafts));
    }

    private static SettingsSectionState State(Draft draft, SettingsContext context)
    {
        // A rule that can't run on what was read, such as values from another build, must not take the snapshot down.
        try
        {
            draft.Problems.AddRange(draft.Definition.FindProblems(draft.Options, context));
            draft.Warnings.AddRange(draft.Definition.FindWarnings(draft.Options, null, context));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or FormatException)
        {
            draft.Problems.Add(new(string.Empty, ServerMessages.SettingsValuesCannotBeChecked.With("error", exception.Message)));
        }

        return new SettingsSectionState
        {
            Definition = draft.Definition,
            Version = draft.Stored?.Version ?? 0,
            UpdatedUtc = draft.Stored?.UpdatedUtc,
            UpdatedBy = draft.Stored?.UpdatedByName,
            Values = draft.Values,
            StoredValues = draft.StoredValues,
            Options = draft.Options,
            Locks = draft.Locks,
            Problems = draft.Problems,
            Warnings = draft.Warnings,
            Secrets = draft.Secrets,
            Stored = draft.Stored,
        };
    }

    private static SettingsInForce InForce(Dictionary<string, SettingsSectionState> sections, Dictionary<string, Draft> drafts) => new(
        MachinesInForce(sections),
        sections[SettingsSectionNames.Ldap].Closed ? Off<LdapOptions>(drafts[SettingsSectionNames.Ldap], ldap => ldap.Enabled = false) : (LdapOptions)drafts[SettingsSectionNames.Ldap].Options,
        sections[SettingsSectionNames.Oidc].Closed ? Off<OidcOptions>(drafts[SettingsSectionNames.Oidc], oidc => oidc.Enabled = false) : (OidcOptions)drafts[SettingsSectionNames.Oidc].Options,
        ForwardedHeadersInForce(sections[SettingsSectionNames.Proxies]),
        sections[SettingsSectionNames.Pxe].Closed ? null : (PxeOptions)drafts[SettingsSectionNames.Pxe].Options,
        LogLevelsInForce(sections[SettingsSectionNames.Logging]));

    private static Draft Read(SettingsSectionDefinition definition, StoredSettingsSection? stored, IConfiguration configuration)
    {
        List<SettingProblem> problems = [];
        JsonObject defaults = definition.Defaults();
        JsonObject configured = Configured(definition, configuration, defaults, problems);
        JsonObject document = stored?.Values ?? [];
        JsonObject values = defaults.DeepClone().AsObject();
        JsonObject storedValues = defaults.DeepClone().AsObject();
        List<SettingLockState> locks = [];
        Dictionary<string, string?> secretValues = new(StringComparer.Ordinal);
        Dictionary<string, SecretState> secrets = new(StringComparer.Ordinal);

        foreach (SettingField field in definition.Fields)
        {
            bool isConfigured = definition.IsLocked(configuration, field);
            string key = definition.ConfigurationKey(field);

            if (field.IsSecret)
            {
                (string? value, SecretState state, bool differs) = ReadSecret(stored?.Secrets.GetValueOrDefault(field.Name), isConfigured ? configuration[key] : null, isConfigured);
                secretValues[field.Name] = value;
                secrets[field.Name] = state;

                if (isConfigured)
                {
                    locks.Add(Lock(field, key, configuration, differs));
                }

                if (state.Unreadable)
                {
                    problems.Add(new(field.Path, ServerMessages.SettingsStoredSecretUnreadable.With()));
                }

                continue;
            }

            JsonNode? storedValue = SettingsJson.TryGet(document, field.Segments, out JsonNode? written) ? written : SettingsJson.Get(defaults, field);
            JsonNode? inForce = isConfigured ? SettingsJson.Get(configured, field) : storedValue;
            SettingsJson.Set(storedValues, field, storedValue);
            SettingsJson.Set(values, field, inForce);

            if (isConfigured)
            {
                locks.Add(Lock(field, key, configuration, !SettingsJson.Same(storedValue, inForce)));
            }
        }

        object options = ReadOptions(definition, values, defaults, secretValues, problems);

        return new Draft(definition, stored, values, storedValues, options, locks, problems, [], secrets, secretValues);
    }

    // Configuration that can't be read counts as the defaults, with a problem that says why. A locked field then takes
    // its default, and the other fields keep their stored values.
    private static JsonObject Configured(SettingsSectionDefinition definition, IConfiguration configuration, JsonObject defaults, List<SettingProblem> problems)
    {
        try
        {
            return definition.Configured(configuration);
        }
        catch (InvalidOperationException exception)
        {
            problems.Add(new(
                string.Empty,
                ServerMessages.SettingsConfigurationUnreadable.With("section", definition.ConfigurationPath, "error", exception.Message)));

            return defaults.DeepClone().AsObject();
        }
    }

    // Value is null when there's no secret. Differs says whether a configured value hides a different stored one.
    private static (string? Value, SecretState State, bool Differs) ReadSecret(StoredSecret? secret, string? configuredSecret, bool isConfigured)
    {
        string? value = isConfigured ? configuredSecret : secret?.Value;
        bool unreadable = !isConfigured && secret?.Unreadable == true;

        return (
            string.IsNullOrEmpty(value) ? null : value,
            new SecretState(!string.IsNullOrEmpty(value), unreadable, isConfigured ? null : secret?.UpdatedUtc),
            !string.Equals(secret?.Value ?? string.Empty, configuredSecret ?? string.Empty, StringComparison.Ordinal));
    }

    private static SettingLockState Lock(SettingField field, string key, IConfiguration configuration, bool storedDiffers) =>
        new(field, key, ConfigurationSources.Describe(configuration, key) ?? "configuration", storedDiffers);

    // A stored value that no longer converts, for example one written by another build, is replaced by its default. The
    // section lists the field as a problem until someone saves it again.
    private static object ReadOptions(
        SettingsSectionDefinition definition,
        JsonObject values,
        JsonObject defaults,
        IReadOnlyDictionary<string, string?> secrets,
        List<SettingProblem> problems)
    {
        try
        {
            return definition.Read(values, secrets);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidOperationException or FormatException)
        {
            foreach (SettingField field in definition.Fields.Where(field => !field.IsSecret))
            {
                JsonObject single = defaults.DeepClone().AsObject();
                SettingsJson.Set(single, field, SettingsJson.Get(values, field));

                try
                {
                    _ = definition.Read(single, secrets);
                }
                catch (Exception fieldException) when (fieldException is JsonException or NotSupportedException or InvalidOperationException or FormatException)
                {
                    SettingsJson.Set(values, field, SettingsJson.Get(defaults, field));
                    problems.Add(new(field.Path, ServerMessages.SettingsStoredValueUnreadable.With("error", fieldException.Message)));
                }
            }

            return definition.Read(values, secrets);
        }
    }

    private static MachinePolicy MachinesInForce(Dictionary<string, SettingsSectionState> sections)
    {
        SettingsSectionState machines = sections[SettingsSectionNames.Machines];
        MachineOptions options = (MachineOptions)machines.Options;
        bool proxiesClosed = sections[SettingsSectionNames.Proxies].Closed;

        if (!machines.Closed)
        {
            return new MachinePolicy(
                options.RequireWebApproval,
                options.MaxWaitingPerAddress,
                options.MaxWaiting,
                proxiesClosed ? ZeroTouchNetworks.None : ZeroTouchNetworks.Parse(options.ZeroTouchNetworks));
        }

        // Web approval stays on if either the page or configuration asked for it, no matter what else is wrong.
        MachineOptions defaults = new();
        SettingField requireWebApproval = SettingsDefinitions.Machines.Field("requireWebApproval")!;

        return new MachinePolicy(
            IsTrue(SettingsJson.Get(machines.StoredValues, requireWebApproval)) || IsTrue(SettingsJson.Get(machines.Values, requireWebApproval)),
            defaults.MaxWaitingPerAddress,
            defaults.MaxWaiting,
            ZeroTouchNetworks.None);
    }

    private static bool IsTrue(JsonNode? node) => node is JsonValue value && value.TryGetValue(out bool flag) && flag;

    private static T Off<T>(Draft draft, Action<T> turnOff)
        where T : class
    {
        T options = (T)draft.Definition.Read(draft.Values, draft.SecretValues);
        turnOff(options);

        return options;
    }

    private static ForwardedHeadersOptions ForwardedHeadersInForce(SettingsSectionState proxies)
    {
        ForwardedHeadersOptions options = new();
        DdtForwardedHeadersExtensions.TrustOnly(options, proxies.Closed ? new DdtForwardedHeadersOptions() : (DdtForwardedHeadersOptions)proxies.Options);

        return options;
    }

    private static Dictionary<string, LogLevel> LogLevelsInForce(SettingsSectionState logging)
    {
        Dictionary<string, string> levels = logging.Closed ? LoggingOptions.Defaults() : ((LoggingOptions)logging.Options).LogLevel;
        Dictionary<string, LogLevel> parsed = new(StringComparer.OrdinalIgnoreCase);

        foreach ((string category, string level) in levels)
        {
            if (LoggingSettingsSection.TryParse(level) is { } value)
            {
                parsed[category] = value;
            }
        }

        return parsed;
    }

    private sealed record Draft(
        SettingsSectionDefinition Definition,
        StoredSettingsSection? Stored,
        JsonObject Values,
        JsonObject StoredValues,
        object Options,
        List<SettingLockState> Locks,
        List<SettingProblem> Problems,
        List<SettingWarning> Warnings,
        Dictionary<string, SecretState> Secrets,
        Dictionary<string, string?> SecretValues);
}
