// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;

namespace DDT.Agent;

// A console that is told each line's level beside its text, as the journal of a systemd unit takes it.
public interface ILeveledOutput
{
    void WriteLine(AgentLogLevel level, string line);
}
