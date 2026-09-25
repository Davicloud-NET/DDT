// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;

namespace DDT.Contracts.Deployments;

// A run with the definition it was given, frozen when it was assigned, its steps and its files. Definition is null
// for a deployment from before task sequences. AllowSecureBootMismatch says whoever started the run let an image that
// is not signed for Secure Boot be written on a machine with Secure Boot on.
public sealed record DeploymentView(
    DeploymentSummary Summary,
    Guid MachineId,
    long? SequenceRevision,
    Guid? RuleId,
    SequenceDefinition? Definition,
    IReadOnlyList<DeploymentStepView> Steps,
    IReadOnlyList<DeploymentArtifactView> Artifacts,
    bool AllowSecureBootMismatch = false);
