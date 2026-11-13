// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Applications;

// Creates or saves an application.
public sealed record SaveApplicationRequest(
    // The revision the page last read. A new application doesn't have one, so the server ignores it.
    long Revision,
    string Name,
    string? Description,
    string? Publisher,
    string? Version,
    ApplicationKind Kind,
    // An application upload's package, or null for none.
    Guid? PackageId,
    string? Command,
    IReadOnlyList<int> SuccessExitCodes,
    IReadOnlyList<int> RestartExitCodes,
    ApplicationDetection? Detection,
    IReadOnlyList<Guid> DependsOn,
    string? WingetId,
    string? WingetVersion,
    int TimeoutMinutes,
    bool ContinueOnFailure);
