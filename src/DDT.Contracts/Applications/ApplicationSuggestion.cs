// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Applications;

// What an uploaded MSI, known EXE installer or MSIX package suggests for a new application. Command is null for MSIX.
// Detection is a ProductCodeDetection, an UninstallKeyDetection, a PackageFamilyDetection or null.
public sealed record ApplicationSuggestion(
    ApplicationKind Kind,
    string? Name,
    string? Publisher,
    string? Version,
    string? Command,
    IReadOnlyList<int> SuccessExitCodes,
    IReadOnlyList<int> RestartExitCodes,
    ApplicationDetection? Detection);
