// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;

namespace DDT.Agent.Deployment;

// The sign-ins of a system that runs nothing as an account. It fails a step that asks for one.
public sealed class NoLogons : IAccountLogons
{
    public const string Message = "This system runs nothing as an account, so a step that names one cannot run in it.";

    public Task<IAccountSession> LogOnAsync(AgentAccount account, CancellationToken cancellationToken) =>
        Task.FromException<IAccountSession>(new DeploymentStepException(Message));
}
