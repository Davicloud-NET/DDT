// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;
using DDT.Contracts.Values;

namespace DDT.Contracts.Deployments;

// A run with its steps, its files and the definition it was given, frozen at assignment.
public sealed record DeploymentView(
    DeploymentSummary Summary,
    Guid MachineId,
    long? SequenceRevision,
    Guid? RuleId,
    // Null for a deployment that predates task sequences.
    SequenceDefinition? Definition,
    IReadOnlyList<DeploymentStepView> Steps,
    IReadOnlyList<DeploymentArtifactView> Artifacts,
    // Whoever started the run allowed writing an image that isn't signed for Secure Boot, even with Secure Boot on.
    bool AllowSecureBootMismatch = false,
    // The values as worked out when the run started, each with where it came from. A secret only shows that it's set.
    IReadOnlyList<ResolvedValue>? Values = null,
    // The sequence's variables as the agent last reported them.
    IReadOnlyDictionary<string, string>? Variables = null,
    // The sequence's inputs and whether they're answered. This and Pause are null when the run has none.
    IReadOnlyList<RunInputView>? Inputs = null,
    RunPauseView? Pause = null);
