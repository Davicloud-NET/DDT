// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Deployments;

// The definition a run was given. It's in its own table, so the run's row, which every report updates, stays small.
public sealed class DeploymentSnapshot
{
    public Guid DeploymentId { get; set; }

    // The SequenceDefinition as DdtJsonContext writes it.
    public required string Definition { get; set; }
}
