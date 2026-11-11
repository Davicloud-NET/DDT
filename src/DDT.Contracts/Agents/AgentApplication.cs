// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Applications;

namespace DDT.Contracts.Agents;

// An application as frozen into a run, in install order. Files is null for one without files, such as winget.
public sealed record AgentApplication(
    Guid Id,
    string Name,
    ApplicationKind Kind,
    string? Command,
    IReadOnlyList<int> SuccessExitCodes,
    IReadOnlyList<int> RestartExitCodes,
    ApplicationDetection? Detection,
    int TimeoutMinutes,
    bool ContinueOnFailure,
    string? WingetId,
    string? WingetVersion,
    AgentApplicationFiles? Files);
