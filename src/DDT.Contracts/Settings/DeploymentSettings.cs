// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Settings;

// The deployment section. Its secrets are productKey, localAdministrator.password and domain.password.
public sealed record DeploymentSettings(
    string? TimeZone,
    string? Locale,
    string? Keyboard,
    LocalAdministratorSettings LocalAdministrator,
    DomainSettings Domain,
    // The language the console at the machine starts in, en or de. Null means the language of WinPE.
    string? ConsoleLanguage = null,
    // The keyboard layout of the console in the Linux boot environment, by its keymap's name, such as de. Null is us.
    string? ConsoleKeyboardLayout = null,
    // RegisteredOrganization and RegisteredOwner in the answer file. Null leaves them out.
    string? Organization = null,
    string? Owner = null);
