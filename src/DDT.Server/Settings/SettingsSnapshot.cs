// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using DDT.Contracts.Messages;
using DDT.Contracts.Settings;
using DDT.Core.Configuration;
using DDT.Pxe;
using DDT.Server.Authentication;
using DDT.Server.Configuration;
using DDT.Server.Deployments;
using DDT.Server.Ldap;
using DDT.Server.Machines;
using DDT.Server.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DDT.Server.Settings;

// Every section, validated, decrypted and parsed, as one immutable value that consumers read once per request or
// decision. Exactly one function builds it, from the stored rows and the configuration that overrides them. A section
// whose values have problems fails closed: the typed members below then hold what is safe, never what was stored.
public sealed class SettingsSnapshot
{
    private readonly Dictionary<string, SettingsSectionState> _sections;

    private SettingsSnapshot(
        Dictionary<string, SettingsSectionState> sections,
        MachinePolicy machines,
        LdapOptions ldap,
        OidcOptions oidc,
        ForwardedHeadersOptions forwardedHeaders,
        PxeOptions? pxe,
        IReadOnlyDictionary<string, LogLevel> logLevels)
    {
        _sections = sections;
        Machines = machines;
        Ldap = ldap;
        Oidc = oidc;
        ForwardedHeaders = forwardedHeaders;
        Pxe = pxe;
        LogLevels = logLevels;
    }

    public IReadOnlyList<SettingsSectionState> Sections => [.. SettingsDefinitions.All.Select(definition => _sections[definition.Name])];

    public SettingsSectionState this[string section] => _sections[section];

    // What applies to runs, secrets included. Consumers refuse new runs while DeploymentProblems is not empty.
    public DeploymentOptions Deployment => (DeploymentOptions)this[SettingsSectionNames.Deployment].Options;

    public IReadOnlyList<SettingProblem> DeploymentProblems => this[SettingsSectionNames.Deployment].Problems;

    public MachinePolicy Machines { get; }

    // Off while the section has problems.
    public LdapOptions Ldap { get; }

    // Off while the section has problems, so the scheme is not registered.
    public OidcOptions Oidc { get; }

    // Trusts nothing while the proxies section has problems.
    public ForwardedHeadersOptions ForwardedHeaders { get; }

    // Null while the section has problems, which keeps the listeners stopped. HttpBootPort and BootDirectory are the
    // configured ones.
    public PxeOptions? Pxe { get; }

    // The code defaults while the section has problems. Default is the level of every other category.
    public IReadOnlyDictionary<string, LogLevel> LogLevels { get; }

    public static SettingsSnapshot Build(IReadOnlyDictionary<string, StoredSettingsSection> stored, IConfiguration configuration, string? saving = null)
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
            // A rule that cannot run on what was read, such as one of another build, must not take the snapshot down.
            try
            {
                draft.Problems.AddRange(draft.Definition.FindProblems(draft.Options, context));
                draft.Warnings.AddRange(draft.Definition.FindWarnings(draft.Options, null, context));
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or FormatException)
            {
                draft.Problems.Add(new(string.Empty, ServerMessages.SettingsValuesCannotBeChecked.With("error", exception.Message)));
            }

            sections[name] = new SettingsSectionState
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

        return new SettingsSnapshot(
            sections,
            MachinesInForce(sections),
            sections[SettingsSectionNames.Ldap].Closed ? Off<LdapOptions>(drafts[SettingsSectionNames.Ldap], ldap => ldap.Enabled = false) : (LdapOptions)drafts[SettingsSectionNames.Ldap].Options,
            sections[SettingsSectionNames.Oidc].Closed ? Off<OidcOptions>(drafts[SettingsSectionNames.Oidc], oidc => oidc.Enabled = false) : (OidcOptions)drafts[SettingsSectionNames.Oidc].Options,
            ForwardedHeadersInForce(sections[SettingsSectionNames.Proxies]),
            sections[SettingsSectionNames.Pxe].Closed ? null : (PxeOptions)drafts[SettingsSectionNames.Pxe].Options,
            LogLevelsInForce(sections[SettingsSectionNames.Logging]));
    }

    private static Draft Read(SettingsSectionDefinition definition, StoredSettingsSection? stored, IConfiguration configuration)
    {
        List<SettingProblem> problems = [];
        JsonObject defaults = definition.Defaults();
        JsonObject configured;

        try
        {
            configured = definition.Configured(configuration);
        }
        catch (InvalidOperationException exception)
        {
            configured = defaults.DeepClone().AsObject();
            problems.Add(new(
                string.Empty,
                ServerMessages.SettingsConfigurationUnreadable.With("section", definition.ConfigurationPath, "error", exception.Message)));
        }

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
                StoredSecret? secret = stored?.Secrets.GetValueOrDefault(field.Name);
                string? configuredSecret = isConfigured ? configuration[key] : null;
                string? value = isConfigured ? configuredSecret : secret?.Value;
                bool unreadable = !isConfigured && secret?.Unreadable == true;

                secretValues[field.Name] = string.IsNullOrEmpty(value) ? null : value;
                secrets[field.Name] = new SecretState(!string.IsNullOrEmpty(value), unreadable, isConfigured ? null : secret?.UpdatedUtc);

                if (isConfigured)
                {
                    locks.Add(new(field, key, ConfigurationSources.Describe(configuration, key) ?? "configuration", !string.Equals(secret?.Value ?? string.Empty, configuredSecret ?? string.Empty, StringComparison.Ordinal)));
                }

                if (unreadable)
                {
                    problems.Add(new(field.Path, ServerMessages.SettingsStoredSecretUnreadable.With()));
                }

                continue;
            }

            JsonNode? storedValue = SettingsJson.TryGet(document, field.Segments, out JsonNode? written) ? written : SettingsJson.Get(defaults, field);
            SettingsJson.Set(storedValues, field, storedValue);

            if (isConfigured)
            {
                JsonNode? configuredValue = SettingsJson.Get(configured, field);
                SettingsJson.Set(values, field, configuredValue);
                locks.Add(new(field, key, ConfigurationSources.Describe(configuration, key) ?? "configuration", !SettingsJson.Same(storedValue, configuredValue)));
            }
            else
            {
                SettingsJson.Set(values, field, storedValue);
            }
        }

        object options = ReadOptions(definition, values, defaults, secretValues, problems);

        return new Draft(definition, stored, values, storedValues, options, locks, problems, [], secrets, secretValues);
    }

    // A stored value that no longer converts, written by another build for example, takes its default, and the section
    // lists the field until someone saves it again.
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

        // Web approval stays on when either the page or configuration asked for it, whichever else is wrong.
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

// Configuration alone decides these, and the pxe section takes them from there.
internal sealed record SettingsBootstrap(int HttpBootPort, string BootDirectory)
{
    public static SettingsBootstrap From(IConfiguration configuration)
    {
        PxeOptions defaults = new();
        string storePath = configuration["DDT:StorePath"] is { Length: > 0 } configuredStore ? configuredStore : new DdtOptions().StorePath;
        int port = int.TryParse(configuration["DDT:Pxe:HttpBootPort"], NumberStyles.Integer, CultureInfo.InvariantCulture, out int configuredPort)
            ? configuredPort
            : defaults.HttpBootPort;
        string bootDirectory = configuration["DDT:Pxe:BootDirectory"] ?? defaults.BootDirectory;

        try
        {
            return new(port, string.IsNullOrWhiteSpace(bootDirectory) ? bootDirectory : PxeSetup.BootDirectoryIn(bootDirectory, storePath));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new(port, bootDirectory);
        }
    }
}

// What the machines section decides once its own problems and those of the proxies are taken into account.
public sealed record MachinePolicy(bool RequireWebApproval, int MaxWaitingPerAddress, int MaxWaiting, ZeroTouchNetworks ZeroTouchNetworks)
{
    // Under RequireWebApproval a netboot always waits for a sign-in, whatever networks are listed.
    public bool ZeroTouchEnabled => !RequireWebApproval && !ZeroTouchNetworks.IsEmpty;
}

// One section in a snapshot. Values is what applies, secrets left out; StoredValues is what the page holds, over the
// defaults, which applies again once configuration stops setting a field.
public sealed class SettingsSectionState
{
    public required SettingsSectionDefinition Definition { get; init; }

    public string Name => Definition.Name;

    public required long Version { get; init; }

    public DateTimeOffset? UpdatedUtc { get; init; }

    public string? UpdatedBy { get; init; }

    public required JsonObject Values { get; init; }

    public required JsonObject StoredValues { get; init; }

    public required object Options { get; init; }

    public required IReadOnlyList<SettingLockState> Locks { get; init; }

    public required IReadOnlyList<SettingProblem> Problems { get; init; }

    public required IReadOnlyList<SettingWarning> Warnings { get; init; }

    public required IReadOnlyDictionary<string, SecretState> Secrets { get; init; }

    public StoredSettingsSection? Stored { get; init; }

    public bool Closed => Problems.Count > 0;

    public bool IsLocked(SettingField field) => Locks.Any(settingLock => settingLock.Field == field);
}

public sealed record SettingLockState(SettingField Field, string ConfigurationKey, string Source, bool StoredDiffers);
