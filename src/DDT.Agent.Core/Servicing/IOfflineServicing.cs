// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Servicing;

// Changes the applied Windows while it is offline. Any failure but the outcomes throws DeploymentStepException. A
// version is the image's as the call found it, null when unreadable.
public interface IOfflineServicing
{
    // Adds the .cab and .msu files in one call. percent is the call's own progress.
    Task<ServicingAnswer> AddAsync(OfflineImage image, IReadOnlyList<string> packages, IProgress<int> percent, CancellationToken cancellationToken);

    // Removes the superseded components an update left behind.
    Task<Version?> CleanupAsync(OfflineImage image, CancellationToken cancellationToken);

    // Never throws for a version it can't read.
    Task<Version?> VersionAsync(OfflineImage image, CancellationToken cancellationToken);
}
