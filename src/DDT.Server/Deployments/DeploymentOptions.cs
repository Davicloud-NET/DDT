// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Deployments;

// What every deployed machine's answer file carries: the deployment section of the settings page, which configuration
// can override, checked by DeploymentOptionsValidation.
public sealed class DeploymentOptions
{
    public const string SectionName = "DDT:Deployment";

    // A Windows id such as W. Europe Standard Time. Unset, Windows picks the zone that matches the locale.
    public string? TimeZone { get; set; }

    // A culture name such as de-DE. Unset, the image's own language.
    public string? Locale { get; set; }

    // An input locale such as 0407:00000407 or de-DE. Unset, the locale.
    public string? Keyboard { get; set; }

    // The language the console at the machine starts in, en or de; the person there can still switch with F5. Unset,
    // the language of Windows PE, which is English in the image copype makes. It is not in the answer file.
    public string? ConsoleLanguage { get; set; }

    public LocalAdministratorOptions LocalAdministrator { get; set; } = new();

    public DomainOptions Domain { get; set; } = new();
}
