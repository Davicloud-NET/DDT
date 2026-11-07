// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;

namespace DDT.Agent.InstalledPhase;

// What a registration gives the service while the server still has the run going.
internal sealed record InstalledPhaseContact(Guid MachineId, DeploymentTokens Tokens, MachineIdentity Identity);
