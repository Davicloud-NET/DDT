// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.WindowsUpdates;

// The Windows Update Agent of the running Windows. A failure of the agent itself throws DeploymentStepException.
public interface IWindowsUpdate
{
    // Windows needs a restart, before or after an installation.
    Task<bool> IsRestartPendingAsync(CancellationToken cancellationToken);

    // Another installation is running.
    Task<bool> IsBusyAsync(CancellationToken cancellationToken);

    // What is not installed and not hidden.
    Task<IReadOnlyList<AvailableUpdate>> SearchAsync(WindowsUpdateSearch search, CancellationToken cancellationToken);

    Task AcceptEulaAsync(AvailableUpdate update, CancellationToken cancellationToken);

    // A stop aborts the download and throws OperationCanceledException at once.
    Task<IReadOnlyList<UpdateOutcome>> DownloadAsync(IReadOnlyList<AvailableUpdate> updates, IProgress<int> percent, CancellationToken cancellationToken);

    // An installation cannot be aborted: a stop asks for it, and this still returns only once the job has ended.
    Task<UpdateInstallation> InstallAsync(IReadOnlyList<AvailableUpdate> updates, IProgress<int> percent, CancellationToken cancellationToken);
}
