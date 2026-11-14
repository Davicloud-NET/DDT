// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;

namespace DDT.Agent.InstalledPhase;

// Sets up the first-logon commands and the automatic logon for the next start of Windows, once DDT's session is gone.
public interface IFirstLogon
{
    void SetUp(AgentFirstLogon firstLogon);
}
