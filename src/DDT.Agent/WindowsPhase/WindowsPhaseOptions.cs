// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.WindowsPhase;

// WindowsRoot is the root of the installed Windows, such as C:\, whose DDT directory holds the run.
internal sealed record WindowsPhaseOptions(string WindowsRoot, string AgentVersion, bool DryRun);
