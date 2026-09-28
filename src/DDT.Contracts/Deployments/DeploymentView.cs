// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;
using DDT.Contracts.Values;

namespace DDT.Contracts.Deployments;

// A run with the definition it was given, frozen when it was assigned, its steps and its files.
public sealed record DeploymentView(
    DeploymentSummary Summary,
    Guid MachineId,
    long? SequenceRevision,
    Guid? RuleId,
    // Null for a deployment that predates task sequences.
    SequenceDefinition? Definition,
    IReadOnlyList<DeploymentStepView> Steps,
    IReadOnlyList<DeploymentArtifactView> Artifacts,
    // Whoever started the run let an image not signed for Secure Boot be written although Secure Boot is on.
    bool AllowSecureBootMismatch = false,
    // As worked out when the run started, each with where it came from; a secret shows only that it is set.
    IReadOnlyList<ResolvedValue>? Values = null,
    // The sequence's variables as the agent last reported them.
    IReadOnlyDictionary<string, string>? Variables = null,
    // The sequence's inputs and whether they are answered. This and Pause are null where the run has none.
    IReadOnlyList<RunInputView>? Inputs = null,
    RunPauseView? Pause = null);
