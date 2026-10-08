// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Deployment;

// A machine's way into the other boot environment: what carries its resume token, and what restarts it.
public sealed record EnvironmentSwitch(ResumeCarrier Carrier, IRebooter Rebooter);
