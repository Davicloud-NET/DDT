// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Applications;

namespace DDT.Agent.Applications;

// Installs the applications of one kind, and says what the exit code means.
public interface IApplicationKindInstaller
{
    ApplicationKind Kind { get; }

    // Throws DeploymentStepException with a sentence when it could not run or ran out of time.
    Task<int> InstallAsync(ApplicationLaunch launch, CancellationToken cancellationToken);

    ApplicationOutcome Judge(AgentApplication application, int exitCode);
}
