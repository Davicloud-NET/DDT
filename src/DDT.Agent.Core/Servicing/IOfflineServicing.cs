// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Servicing;

// Changes the applied Windows while it is offline. Any failure but the outcomes throws DeploymentStepException.
public interface IOfflineServicing
{
    // Adds the .cab and .msu files in one call. percent is the call's own progress.
    Task<ServicingOutcome> AddAsync(OfflineImage image, IReadOnlyList<string> packages, IProgress<int> percent, CancellationToken cancellationToken);

    // Removes the superseded components an update left behind.
    Task CleanupAsync(OfflineImage image, CancellationToken cancellationToken);
}
