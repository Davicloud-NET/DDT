// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent;

// Frozen range: an agent from an older boot image starts newer ones and passes their exit codes on, and takes any
// code outside 0 to HighestAgentCode, or ConfigurationError, to mean the new agent could not run at all. So
// ConfigurationError may only ever mean that the arguments or agent.json could not be read.
public static class AgentExitCodes
{
    public const int Stopped = 0;
    public const int Rejected = 2;
    public const int ConfigurationError = 3;
    public const int Deployed = 4;

    // The machine restarts in the middle of a run, which goes on from its state on the disk after the restart.
    public const int Restarting = 5;

    public const int HighestAgentCode = 63;
}
