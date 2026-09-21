// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Deployments;

namespace DDT.Contracts.Agents;

// DiskNumber is the disk chosen at the machine; a deployment assigned on the web has none.
public sealed record AgentDeployment(
    Guid Id,
    DeploymentState State,
    Guid ImageId,
    string ImageName,
    string Sha256,
    long SizeBytes,
    int WimIndex,
    long InstalledBytes,
    int? DiskNumber);
