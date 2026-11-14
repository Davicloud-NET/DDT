// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;

namespace DDT.Agent.Servicing;

// Adds nothing, and takes every package as added, so a dry run goes through the whole step.
public sealed class DryRunOfflineServicing(AgentLog log) : IOfflineServicing
{
    public Task<ServicingOutcome> AddAsync(OfflineImage image, IReadOnlyList<string> packages, IProgress<int> percent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(packages);
        ArgumentNullException.ThrowIfNull(percent);

        log.Information($"Dry run: {string.Join(", ", packages.Select(Path.GetFileName))} not added, and taken as added.");
        percent.Report(100);

        return Task.FromResult(ServicingOutcome.Added);
    }

    public Task CleanupAsync(OfflineImage image, CancellationToken cancellationToken)
    {
        log.Information("Dry run: the image is not cleaned up.");

        return Task.CompletedTask;
    }
}
