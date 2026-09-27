// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization.Metadata;
using DDT.Contracts.Settings;
using DDT.Core.Configuration;
using DDT.Server.Deployments;
using Microsoft.Extensions.Configuration;

namespace DDT.Server.Settings;

public sealed class DeploymentSettingsSection() : SettingsSectionDefinition<DeploymentOptions>(
    SettingsSectionNames.Deployment,
    DeploymentOptions.SectionName,
    SettingsSectionKind.Live,
    [
        new("TimeZone"),
        new("Locale"),
        new("Keyboard"),
        new("ConsoleLanguage"),
        new("LocalAdministrator:Name"),
        new("LocalAdministrator:Password", SettingFieldKind.Secret),
        new("Domain:Name"),
        new("Domain:OrganizationalUnit"),
        new("Domain:UserName"),
        new("Domain:Password", SettingFieldKind.Secret),
        new("Domain:Controller"),
    ])
{
    protected override JsonTypeInfo<DeploymentOptions> TypeInfo => SettingsJsonContext.Default.DeploymentOptions;

    protected override DeploymentOptions? Bind(IConfigurationSection section) => section.Get<DeploymentOptions>();

    protected override void SetSecret(DeploymentOptions options, SettingField field, string? value)
    {
        switch (field.Path)
        {
            case "LocalAdministrator:Password":
                options.LocalAdministrator.Password = value;
                break;
            case "Domain:Password":
                options.Domain.Password = value;
                break;
            default:
                base.SetSecret(options, field, value);
                break;
        }
    }

    protected override string? GetSecret(DeploymentOptions options, SettingField field) => field.Path switch
    {
        "LocalAdministrator:Password" => options.LocalAdministrator.Password,
        "Domain:Password" => options.Domain.Password,
        _ => base.GetSecret(options, field),
    };

    protected override IReadOnlyList<SettingProblem> FindProblems(DeploymentOptions options, SettingsContext context) =>
        DeploymentOptionsValidation.FindProblems(options);
}
