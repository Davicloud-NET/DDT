// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent;

// Root stands in for the machine's disks and survives every restart. The same dry run id uses the same root.
// AgentPath is the agent that the hand-over stages into the Windows under Root.
public sealed record DryRunMachineOptions(AgentOptions Agent, string Root, string AgentPath, TimeSpan HeartbeatInterval, string AgentVersion);
