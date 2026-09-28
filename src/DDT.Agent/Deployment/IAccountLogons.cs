// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;

namespace DDT.Agent.Deployment;

public interface IAccountLogons
{
    // Signs the account in on this computer. A refusal throws DeploymentStepException, which names the account and says
    // why, never with the password.
    Task<IAccountSession> LogOnAsync(AgentAccount account, CancellationToken cancellationToken);
}
