// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Applications;

// What an uploaded MSI or known EXE installer suggests for a new application. Detection is a ProductCodeDetection, an
// UninstallKeyDetection or null.
public sealed record ApplicationSuggestion(
    string? Name,
    string? Publisher,
    string? Version,
    string Command,
    IReadOnlyList<int> SuccessExitCodes,
    IReadOnlyList<int> RestartExitCodes,
    ApplicationDetection? Detection);
