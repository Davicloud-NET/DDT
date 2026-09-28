// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Deployments;

namespace DDT.Server.Deployments;

// A page of the run history, bound from the query string. Before is the cursor a page handed out.
internal sealed record RunHistoryQuery(
    DeploymentState[]? State,
    Guid? SequenceId,
    Guid? MachineId,
    string? Query,
    string? Before,
    int? Limit);
