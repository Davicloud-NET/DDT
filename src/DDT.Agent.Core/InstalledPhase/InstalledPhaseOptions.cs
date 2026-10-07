// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.InstalledPhase;

// Root is the root of the installed system, such as C:\, whose run directory holds the run.
internal sealed record InstalledPhaseOptions(InstalledPhaseProfile Profile, string Root, string AgentVersion, bool DryRun);
