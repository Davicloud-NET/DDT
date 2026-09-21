// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Deployments;

namespace DDT.Server.Deployments;

public sealed class Deployment
{
    public Guid Id { get; set; }

    public Guid MachineId { get; set; }

    // Cleared when the image is deleted; the copies below keep the history readable.
    public Guid? ImageId { get; set; }

    public required string ImageName { get; set; }

    public required string Sha256 { get; set; }

    public long SizeBytes { get; set; }

    public int WimIndex { get; set; }

    public long InstalledBytes { get; set; }

    public int? DiskNumber { get; set; }

    public DeploymentState State { get; set; } = DeploymentState.Assigned;

    public DeploymentStep? Step { get; set; }

    public int Percent { get; set; }

    public DeploymentSource Source { get; set; }

    public Guid? RequestedByUserId { get; set; }

    public string? RequestedByName { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset? StartedUtc { get; set; }

    public DateTimeOffset? FinishedUtc { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; }

    public string? Error { get; set; }
}
