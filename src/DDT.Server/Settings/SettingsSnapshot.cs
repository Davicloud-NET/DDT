// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Core.Configuration;
using DDT.Pxe;
using DDT.Server.Authentication;
using DDT.Server.Deployments;
using DDT.Server.Ldap;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DDT.Server.Settings;

// Every section validated, decrypted and parsed, as one immutable value consumers read once per request or decision. A
// section with problems fails closed: the typed members then hold what is safe, never what was stored.
public sealed class SettingsSnapshot
{
    private readonly Dictionary<string, SettingsSectionState> _sections;
    private readonly SettingsInForce _inForce;

    internal SettingsSnapshot(Dictionary<string, SettingsSectionState> sections, SettingsInForce inForce)
    {
        _sections = sections;
        _inForce = inForce;
    }

    public IReadOnlyList<SettingsSectionState> Sections => [.. SettingsDefinitions.All.Select(definition => _sections[definition.Name])];

    public SettingsSectionState this[string section] => _sections[section];

    // What applies to runs, secrets included. Consumers refuse new runs while DeploymentProblems is not empty.
    public DeploymentOptions Deployment => (DeploymentOptions)this[SettingsSectionNames.Deployment].Options;

    public IReadOnlyList<SettingProblem> DeploymentProblems => this[SettingsSectionNames.Deployment].Problems;

    public MachinePolicy Machines => _inForce.Machines;

    // Off while the section has problems.
    public LdapOptions Ldap => _inForce.Ldap;

    // Off while the section has problems, so the scheme is not registered.
    public OidcOptions Oidc => _inForce.Oidc;

    // Trusts nothing while the proxies section has problems.
    public ForwardedHeadersOptions ForwardedHeaders => _inForce.ForwardedHeaders;

    // Null while the section has problems, which keeps the listeners stopped. HttpBootPort and BootDirectory are the
    // configured ones.
    public PxeOptions? Pxe => _inForce.Pxe;

    // The code defaults while the section has problems. Default is the level of every other category.
    public IReadOnlyDictionary<string, LogLevel> LogLevels => _inForce.LogLevels;

    // From the stored rows and the configuration that overrides them; Saving names the section a save previews.
    public static SettingsSnapshot Build(IReadOnlyDictionary<string, StoredSettingsSection> stored, IConfiguration configuration, string? saving = null) =>
        SettingsSnapshotBuilder.Build(stored, configuration, saving);
}
