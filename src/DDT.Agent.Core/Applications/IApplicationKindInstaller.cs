// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Applications;

namespace DDT.Agent.Applications;

// Installs the applications of one kind. The application's success and restart codes judge the exit code.
public interface IApplicationKindInstaller
{
    ApplicationKind Kind { get; }

    // Throws DeploymentStepException with a sentence when it could not run or ran out of time.
    Task<int> InstallAsync(ApplicationLaunch launch, CancellationToken cancellationToken);
}
