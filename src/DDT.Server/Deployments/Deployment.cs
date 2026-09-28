// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;

namespace DDT.Server.Deployments;

// A run of a task sequence on one machine. The definition it runs and the files it downloads are frozen when it is
// assigned (DeploymentSnapshot, DeploymentStep, DeploymentArtifact), so editing the sequence never changes a run.
public sealed class Deployment
{
    public Guid Id { get; set; }

    public Guid MachineId { get; set; }

    // Cleared when the sequence is deleted; the run keeps its snapshot.
    public Guid? TaskSequenceId { get; set; }

    public long? SequenceRevision { get; set; }

    // The rule that chose the sequence, without a foreign key, so the history outlives the rule.
    public Guid? RuleId { get; set; }

    // The sequence's name when the run was assigned. A deployment from before task sequences keeps its image's name.
    public required string Title { get; set; }

    public int? DiskNumber { get; set; }

    public DeploymentState State { get; set; } = DeploymentState.Assigned;

    public DeploymentSource Source { get; set; }

    public Guid? RequestedByUserId { get; set; }

    public string? RequestedByName { get; set; }

    // The steps that do something, the leaves of the run's tree, as a list of runs counts them: a group, an IF or a
    // repeat is not a step of its own.
    public int StepCount { get; set; }

    // The step the agent reported last, and its percent. The index counts the steps before it as StepCount does, so for
    // a tree it is not the row's Index, which counts every node.
    public int? CurrentStepIndex { get; set; }

    public string? CurrentStepName { get; set; }

    public int Percent { get; set; }

    public SequencePhase? CurrentPhase { get; set; }

    public RunActivity? Activity { get; set; }

    // RunInputs as JSON, taken when the run started: the settings its answer file and domain join use. Never a secret.
    public string? Inputs { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset? StartedUtc { get; set; }

    public DateTimeOffset? FinishedUtc { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; }

    public string? Error { get; set; }

    // Whoever started the run let a raw disk image that is not signed for Secure Boot be written on a machine that
    // has Secure Boot on. A rule's run never has it.
    public bool AllowSecureBootMismatch { get; set; }

    // The answers to the sequence's inputs, see RunAnswer, with who gave each and when. An Account input's answer is a
    // RunCredential and never here. Null before the first answer.
    public string? Answers { get; set; }

    // The run's values as they were worked out when it started, as DdtJsonContext writes a list of ResolvedValue. Never a
    // secret: accounts are RunCredential rows or Account rows.
    public string? Values { get; set; }

    // The sequence's variables as the agent last reported them, as DdtJsonContext writes a map of name to value.
    public string? Variables { get; set; }

    // The run waits at its start for answers to required inputs.
    public bool InputsPending { get; set; }

    // The Pause step the run waits at, its pass, and the message the agent worked out for it; null while it waits at
    // none.
    public Guid? PauseStepId { get; set; }

    public int? PausePass { get; set; }

    public string? PauseMessage { get; set; }

    // The pause someone continued on the web, which the answer to the agent's next report names, and who continued it.
    public Guid? ContinueStepId { get; set; }

    public int? ContinuePass { get; set; }

    public string? ContinuedByName { get; set; }
}
