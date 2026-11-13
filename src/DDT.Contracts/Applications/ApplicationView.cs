// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Applications;

// An application in the library.
public sealed record ApplicationView(
    Guid Id,
    string Name,
    string? Description,
    string? Publisher,
    string? Version,
    ApplicationKind Kind,
    ApplicationFiles? Files,
    string? Command,
    IReadOnlyList<int> SuccessExitCodes,
    IReadOnlyList<int> RestartExitCodes,
    // Null installs every time.
    ApplicationDetection? Detection,
    // Installed first, in this order.
    IReadOnlyList<Guid> DependsOn,
    string? WingetId,
    // Null is the latest.
    string? WingetVersion,
    int TimeoutMinutes,
    // The run goes on if this fails.
    bool ContinueOnFailure,
    // Bundles, the applications that depend on it, then sequences, each by name.
    IReadOnlyList<ApplicationUse> UsedBy,
    long Revision,
    DateTimeOffset UpdatedUtc,
    string? UpdatedBy);
