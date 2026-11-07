// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Consoles;

namespace DDT.Agent.Sequences;

// What one run shares with the runners of its steps. Console is null when the run shows on no console.
public sealed record StepRunnerParts(
    RunSession Session,
    FileRunStateStore Store,
    RunHeartbeat Heartbeat,
    RunDownloads Downloads,
    IMachineConsole? Console);
