// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Agents;

// Windows is the agent running as the temporary service in the installed Windows, to finish a run there.
public enum AgentEnvironment
{
    WindowsPE,
    Windows,
}
