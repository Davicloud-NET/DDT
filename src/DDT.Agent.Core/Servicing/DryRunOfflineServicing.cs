// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;

namespace DDT.Agent.Servicing;

// Adds nothing, and takes every package as added, so a dry run goes through the whole step. It reads no version, so
// the step says nothing about the build.
public sealed class DryRunOfflineServicing(AgentLog log) : IOfflineServicing
{
    public Task<ServicingAnswer> AddAsync(OfflineImage image, IReadOnlyList<string> packages, IProgress<int> percent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(packages);
        ArgumentNullException.ThrowIfNull(percent);

        log.Information($"Dry run: {string.Join(", ", packages.Select(Path.GetFileName))} not added, and taken as added.");
        percent.Report(100);

        return Task.FromResult(new ServicingAnswer(ServicingOutcome.Added, null));
    }

    public Task<Version?> CleanupAsync(OfflineImage image, CancellationToken cancellationToken)
    {
        log.Information("Dry run: the image is not cleaned up.");

        return Task.FromResult<Version?>(null);
    }

    public Task<Version?> VersionAsync(OfflineImage image, CancellationToken cancellationToken) => Task.FromResult<Version?>(null);
}
