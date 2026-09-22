// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Deployments;

// A file the run downloads, frozen when the run was assigned. SourceId is the image or package it came from, which
// may have been deleted since.
public sealed record DeploymentArtifactView(Guid StepId, ArtifactKind Kind, Guid SourceId, string Name, string Sha256, long SizeBytes);
