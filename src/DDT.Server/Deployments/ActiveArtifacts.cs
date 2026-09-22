// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Deployments;
using DDT.Server.Data;

namespace DDT.Server.Deployments;

public static class ActiveArtifacts
{
    // The artifacts of every run that is assigned or running, which a machine may still download.
    public static IQueryable<DeploymentArtifact> Of(DdtDbContext database)
    {
        ArgumentNullException.ThrowIfNull(database);

        return from artifact in database.DeploymentArtifacts
               join deployment in database.Deployments on artifact.DeploymentId equals deployment.Id
               where deployment.State == DeploymentState.Assigned || deployment.State == DeploymentState.Running
               select artifact;
    }
}
