// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Applications.Installers;

// A silent install the library proposes for an upload. ProductCode suggests an MSI detection rule, UninstallKey one on
// the uninstall key a Burn bundle, an Inno Setup or an NSIS installer registers, in either registry view.
public sealed record InstallerSuggestion(
    string CommandLine,
    IReadOnlyList<int> SuccessExitCodes,
    IReadOnlyList<int> RestartExitCodes,
    string? ProductCode,
    string? UninstallKey);
