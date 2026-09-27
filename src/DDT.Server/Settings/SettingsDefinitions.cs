// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Settings;

// Every section the page edits, in the order it lists them.
public static class SettingsDefinitions
{
    public static DeploymentSettingsSection Deployment { get; } = new();

    public static MachineSettingsSection Machines { get; } = new();

    public static LdapSettingsSection Ldap { get; } = new();

    public static OidcSettingsSection Oidc { get; } = new();

    public static ProxySettingsSection Proxies { get; } = new();

    public static PxeSettingsSection Pxe { get; } = new();

    public static LoggingSettingsSection Logging { get; } = new();

    public static CertificateSettingsSection Certificate { get; } = new();

    public static IReadOnlyList<SettingsSectionDefinition> All { get; } = [Deployment, Machines, Ldap, Oidc, Proxies, Pxe, Logging, Certificate];

    public static SettingsSectionDefinition? Find(string name) =>
        All.FirstOrDefault(definition => string.Equals(definition.Name, name, StringComparison.Ordinal));
}
