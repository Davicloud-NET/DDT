// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;

namespace DDT.Agent.WindowsUpdates;

// Stands in until the Windows Update Agent is reached through COM: every call fails the step.
public sealed class UnbuiltWindowsUpdate : IWindowsUpdate
{
    public const string NotBuilt = "Windows Update is not built into this agent yet.";

    public Task<bool> IsRestartPendingAsync(CancellationToken cancellationToken) => throw new DeploymentStepException(NotBuilt);

    public Task<bool> IsBusyAsync(CancellationToken cancellationToken) => throw new DeploymentStepException(NotBuilt);

    public Task<IReadOnlyList<AvailableUpdate>> SearchAsync(WindowsUpdateSearch search, CancellationToken cancellationToken) =>
        throw new DeploymentStepException(NotBuilt);

    public Task AcceptEulaAsync(AvailableUpdate update, CancellationToken cancellationToken) => throw new DeploymentStepException(NotBuilt);

    public Task<IReadOnlyList<UpdateOutcome>> DownloadAsync(IReadOnlyList<AvailableUpdate> updates, IProgress<int> percent, CancellationToken cancellationToken) =>
        throw new DeploymentStepException(NotBuilt);

    public Task<UpdateInstallation> InstallAsync(IReadOnlyList<AvailableUpdate> updates, IProgress<int> percent, CancellationToken cancellationToken) =>
        throw new DeploymentStepException(NotBuilt);
}
