// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Deployments;
using DDT.Contracts.Images;

namespace DDT.Server.Deployments;

// A file a run downloads, resolved when the run is created and frozen with it. While the run is assigned or running,
// neither its source nor the stored file can be deleted.
public sealed class DeploymentArtifact
{
    public long Id { get; set; }

    public Guid DeploymentId { get; set; }

    public Guid StepId { get; set; }

    public ArtifactKind Kind { get; set; }

    // The image or package it came from, without a foreign key, so the run's history outlives a deletion.
    public Guid SourceId { get; set; }

    public required string Name { get; set; }

    public required string Sha256 { get; set; }

    public long SizeBytes { get; set; }

    // Unpacked or applied, the bytes it takes on the machine's disk.
    public long ExpandedBytes { get; set; }

    public int? WimIndex { get; set; }

    // For a raw disk image: whether it starts with Secure Boot on, as the library judged it.
    public ImageBootCapability? BootCapability { get; set; }

    public string? Language { get; set; }
}
