// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;

namespace DDT.Agent.WindowsPhase;

// What a registration that the server still runs the run for gives the service: the machine, its tokens, and the
// identity it registered with, which the run's conditions read.
internal sealed record WindowsRunContact(Guid MachineId, DeploymentTokens Tokens, MachineIdentity Identity);
