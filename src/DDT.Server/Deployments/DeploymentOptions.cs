// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Deployments;

// What every deployed machine's answer file carries. It's the deployment section of the settings page, which
// configuration can override. DeploymentOptionsValidation checks it.
public sealed class DeploymentOptions
{
    public const string SectionName = "DDT:Deployment";

    // A Windows time zone ID such as W. Europe Standard Time. If unset, Windows picks the zone that matches the locale.
    public string? TimeZone { get; set; }

    // A culture name such as de-DE. If unset, the image's own language is used.
    public string? Locale { get; set; }

    // An input locale such as 0407:00000407 or de-DE. If unset, the locale is used.
    public string? Keyboard { get; set; }

    // The language the console at the machine starts in, en or de. The person there can still switch with F5. If unset,
    // it's WinPE's language, which is English in the image copype makes. It isn't in the answer file.
    public string? ConsoleLanguage { get; set; }

    public LocalAdministratorOptions LocalAdministrator { get; set; } = new();

    public DomainOptions Domain { get; set; } = new();
}
