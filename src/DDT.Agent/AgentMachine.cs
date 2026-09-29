// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Agent.Sequences;

namespace DDT.Agent;

// This agent and the machine it runs on, as the loop sees them. A dry run and the tests fake the machine.
public sealed record AgentMachine(IMachineIdentityReader Identity, IDiskPartitioner Disks, LocalRunLocator Runs, string AgentVersion);
