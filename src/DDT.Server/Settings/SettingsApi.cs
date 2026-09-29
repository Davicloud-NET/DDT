// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Settings;
using DDT.Pxe;
using DDT.Server.Authentication;
using DDT.Server.Configuration;
using DDT.Server.Deployments;
using DDT.Server.Ldap;
using DDT.Server.Machines;
using DDT.Server.Security;

namespace DDT.Server.Settings;

public static class SettingsApi
{
    public static SettingsSectionApi<DeploymentSettings> Deployment { get; } = new(
        SettingsDefinitions.Deployment,
        options => options is DeploymentOptions deployment
            ? new DeploymentSettings(
                deployment.TimeZone,
                deployment.Locale,
                deployment.Keyboard,
                new LocalAdministratorSettings(deployment.LocalAdministrator.Name),
                new DomainSettings(deployment.Domain.Name, deployment.Domain.OrganizationalUnit, deployment.Domain.UserName, deployment.Domain.Controller),
                deployment.ConsoleLanguage)
            : throw Unexpected(options),
        values => new DeploymentOptions
        {
            TimeZone = Optional(values.TimeZone),
            Locale = Optional(values.Locale),
            Keyboard = Optional(values.Keyboard),
            ConsoleLanguage = Optional(values.ConsoleLanguage),
            LocalAdministrator = new LocalAdministratorOptions { Name = values.LocalAdministrator?.Name?.Trim() ?? string.Empty },
            Domain = new DomainOptions
            {
                Name = Optional(values.Domain?.Name),
                OrganizationalUnit = Optional(values.Domain?.OrganizationalUnit),
                UserName = Optional(values.Domain?.UserName),
                Controller = Optional(values.Domain?.Controller),
            },
        });

    public static SettingsSectionApi<MachineSettings> Machines { get; } = new(
        SettingsDefinitions.Machines,
        options => options is MachineOptions machines
            ? new MachineSettings(machines.RequireWebApproval, machines.MaxWaitingPerAddress, machines.MaxWaiting, Split(machines.ZeroTouchNetworks))
            : throw Unexpected(options),
        values => new MachineOptions
        {
            RequireWebApproval = values.RequireWebApproval,
            MaxWaitingPerAddress = values.MaxWaitingPerAddress,
            MaxWaiting = values.MaxWaiting,
            ZeroTouchNetworks = Join(values.ZeroTouchNetworks),
        });

    public static SettingsSectionApi<LdapSettings> Ldap { get; } = new(
        SettingsDefinitions.Ldap,
        options => options is LdapOptions ldap
            ? new LdapSettings(
                ldap.Enabled,
                ldap.Host,
                ldap.Port,
                ldap.Transport switch
                {
                    LdapTransport.StartTls => DirectoryTransport.StartTls,
                    LdapTransport.UnencryptedDangerous => DirectoryTransport.UnencryptedDangerous,
                    _ => DirectoryTransport.Ldaps,
                },
                ldap.BaseDn,
                ldap.BindDn,
                ldap.UserFilter,
                ldap.ImmutableIdAttribute,
                ldap.DisplayNameAttribute,
                ldap.EmailAttribute,
                ldap.ResolveNestedGroups,
                new Dictionary<string, string>(ldap.GroupRoleMap, StringComparer.OrdinalIgnoreCase),
                ldap.Timeout)
            : throw Unexpected(options),
        values => new LdapOptions
        {
            Enabled = values.Enabled,
            Host = values.Host?.Trim() ?? string.Empty,
            Port = values.Port,
            Transport = values.Transport switch
            {
                DirectoryTransport.StartTls => LdapTransport.StartTls,
                DirectoryTransport.UnencryptedDangerous => LdapTransport.UnencryptedDangerous,
                _ => LdapTransport.Ldaps,
            },
            BaseDn = values.BaseDn?.Trim() ?? string.Empty,
            BindDn = values.BindDn?.Trim() ?? string.Empty,
            UserFilter = values.UserFilter?.Trim() ?? string.Empty,
            ImmutableIdAttribute = values.ImmutableIdAttribute?.Trim() ?? string.Empty,
            DisplayNameAttribute = values.DisplayNameAttribute?.Trim() ?? string.Empty,
            EmailAttribute = values.EmailAttribute?.Trim() ?? string.Empty,
            ResolveNestedGroups = values.ResolveNestedGroups,
            GroupRoleMap = SettingsCollections.IgnoringCase((values.GroupRoleMap ?? new Dictionary<string, string>())
                .Select(entry => KeyValuePair.Create(entry.Key.Trim(), entry.Value?.Trim() ?? string.Empty))),
            Timeout = values.Timeout,
        });

    public static SettingsSectionApi<OidcSettings> Oidc { get; } = new(
        SettingsDefinitions.Oidc,
        options => options is OidcOptions oidc
            ? new OidcSettings(
                oidc.Enabled,
                oidc.Authority,
                oidc.ClientId,
                oidc.DisplayName,
                [.. oidc.Scopes],
                oidc.AutoProvision,
                oidc.AutoProvisionRole,
                oidc.GroupsClaim,
                new Dictionary<string, string>(oidc.GroupRoleMap, StringComparer.OrdinalIgnoreCase))
            : throw Unexpected(options),
        values => new OidcOptions
        {
            Enabled = values.Enabled,
            Authority = values.Authority?.Trim() ?? string.Empty,
            ClientId = values.ClientId?.Trim() ?? string.Empty,
            DisplayName = values.DisplayName?.Trim() ?? string.Empty,
            Scopes = [.. (values.Scopes ?? []).Select(scope => scope.Trim()).Where(scope => scope.Length > 0)],
            AutoProvision = values.AutoProvision,
            AutoProvisionRole = values.AutoProvisionRole?.Trim() ?? string.Empty,
            GroupsClaim = values.GroupsClaim?.Trim() ?? string.Empty,
            GroupRoleMap = SettingsCollections.IgnoringCase((values.GroupRoleMap ?? new Dictionary<string, string>())
                .Select(entry => KeyValuePair.Create(entry.Key.Trim(), entry.Value?.Trim() ?? string.Empty))),
        });

    public static SettingsSectionApi<ProxySettings> Proxies { get; } = new(
        SettingsDefinitions.Proxies,
        options => options is DdtForwardedHeadersOptions proxies
            ? new ProxySettings(Split(proxies.KnownProxies), Split(proxies.KnownNetworks))
            : throw Unexpected(options),
        values => new DdtForwardedHeadersOptions { KnownProxies = Join(values.KnownProxies), KnownNetworks = Join(values.KnownNetworks) });

    public static SettingsSectionApi<PxeSettings> Pxe { get; } = new(
        SettingsDefinitions.Pxe,
        options => options is PxeOptions pxe
            ? new PxeSettings(
                Split(pxe.Interfaces),
                pxe.EnableProxyDhcp,
                pxe.EnableTftp,
                pxe.TftpSinglePort,
                pxe.TftpMaxWindowSize,
                pxe.MaxConcurrentTftpTransfers,
                Split(pxe.AuthorisedRelayAgents),
                pxe.BootTargets.ToDictionary(
                    target => target.Key,
                    target => new BootTargetSettings(
                        target.Value.Method,
                        target.Value.BootFile,
                        target.Value.ServerAddress,
                        target.Value.ServerHostName,
                        target.Value.AdvertiseBootServerDiscovery),
                    StringComparer.OrdinalIgnoreCase))
            : throw Unexpected(options),
        values => new PxeOptions
        {
            Interfaces = Join(values.Interfaces),
            EnableProxyDhcp = values.EnableProxyDhcp,
            EnableTftp = values.EnableTftp,
            TftpSinglePort = values.TftpSinglePort,
            TftpMaxWindowSize = values.TftpMaxWindowSize,
            MaxConcurrentTftpTransfers = values.MaxConcurrentTftpTransfers,
            AuthorisedRelayAgents = Join(values.AuthorisedRelayAgents),
            BootTargets = SettingsCollections.IgnoringCase((values.BootTargets ?? new Dictionary<string, BootTargetSettings>())
                .Select(target => KeyValuePair.Create(target.Key.Trim(), new BootTargetOptions
                {
                    Method = Optional(target.Value?.Method),
                    BootFile = Optional(target.Value?.BootFile),
                    ServerAddress = Optional(target.Value?.ServerAddress),
                    ServerHostName = Optional(target.Value?.ServerHostName),
                    AdvertiseBootServerDiscovery = target.Value?.AdvertiseBootServerDiscovery ?? false,
                }))),
        });

    public static SettingsSectionApi<LoggingSettings> Logging { get; } = new(
        SettingsDefinitions.Logging,
        options => options is LoggingOptions logging
            ? new LoggingSettings(new Dictionary<string, string>(logging.LogLevel, StringComparer.OrdinalIgnoreCase))
            : throw Unexpected(options),
        values => new LoggingOptions
        {
            LogLevel = SettingsCollections.IgnoringCase((values.LogLevel ?? new Dictionary<string, string>())
                .Select(level => KeyValuePair.Create(level.Key.Trim(), level.Value?.Trim() ?? string.Empty))),
        });

    public static SettingsSectionApi<CertificateSettings> Certificate { get; } = new(
        SettingsDefinitions.Certificate,
        options => options is HttpsOptions https
            ? new CertificateSettings(CertificateSettingsSection.Names(https.SubjectAlternativeNames))
            : throw Unexpected(options),
        values => new HttpsOptions { SubjectAlternativeNames = Join(values.SubjectAlternativeNames) });

    public static IReadOnlyList<SettingsSectionApi> All { get; } = [Deployment, Machines, Ldap, Oidc, Proxies, Pxe, Logging, Certificate];

    public static SettingsSectionApi? Find(string name) => All.FirstOrDefault(api => api.Name == name);

    // Configuration keeps these lists as comma separated text, so one key or environment variable sets a whole list.
    private static List<string> Split(string value) =>
        [.. value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    private static string Join(IEnumerable<string>? values) =>
        string.Join(", ", (values ?? []).Select(value => value?.Trim()).Where(value => !string.IsNullOrEmpty(value)));

    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static InvalidOperationException Unexpected(object options) =>
        new($"{options.GetType().Name} is not the options of this section.");
}
