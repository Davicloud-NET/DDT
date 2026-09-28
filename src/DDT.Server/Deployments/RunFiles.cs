// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Data;
using DDT.Server.Images;
using DDT.Server.Machines;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Deployments;

// The files frozen with a machine's active run, so a machine never reads the library at large.
internal sealed class RunFiles(DdtDbContext database, ImageStore store)
{
    // Artifact is null when the run has no such file; Path is null when the library lost it. The stored hash, never the
    // one asked for, names the file.
    public async Task<(DeploymentArtifact? Artifact, string? Path)> FindAsync(
        Machine machine,
        Guid runId,
        string sha256,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(sha256);

        string hash = sha256.ToLowerInvariant();
        DeploymentArtifact? artifact = machine.ActiveDeploymentId == runId
            ? await database.DeploymentArtifacts
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.DeploymentId == runId && a.Sha256 == hash, cancellationToken)
                .ConfigureAwait(false)
            : null;

        if (artifact is null)
        {
            return (null, null);
        }

        string path = store.ObjectPath(artifact.Sha256);

        return (artifact, File.Exists(path) ? path : null);
    }
}
