// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Agents;

// The run of a machine that goes on in its installed system, as the server answers a boot environment's agent that
// asked. Detours counts the starts from the network in a row with no call from the installed system between them, this
// one included. At MaxDetours the server ends the run.
public sealed record AgentInstalledRun(Guid RunId, int Detours, int MaxDetours);
