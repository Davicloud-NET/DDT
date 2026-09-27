// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Settings;

// The section deployment. Secrets: localAdministrator.password and domain.password. ConsoleLanguage is en or de, the
// language the console at the machine starts in, or null for the language of Windows PE.
public sealed record DeploymentSettings(
    string? TimeZone,
    string? Locale,
    string? Keyboard,
    LocalAdministratorSettings LocalAdministrator,
    DomainSettings Domain,
    string? ConsoleLanguage = null);

public sealed record LocalAdministratorSettings(string Name);

public sealed record DomainSettings(string? Name, string? OrganizationalUnit, string? UserName, string? Controller);
