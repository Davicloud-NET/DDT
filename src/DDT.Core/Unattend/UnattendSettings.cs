// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Core.Unattend;

// There's no domain join here. A machine joins its domain in Windows, with credentials fetched while that step runs.
// That way the join account's password never lands in Panther\unattend.xml.
public sealed record UnattendSettings(
    string ProcessorArchitecture,
    string ComputerName,
    string? TimeZone,
    string UiLanguage,
    string Locale,
    string Keyboard,
    LocalAdministrator? LocalAdministrator);
