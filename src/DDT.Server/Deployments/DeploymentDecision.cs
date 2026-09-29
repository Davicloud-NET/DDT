// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Messages;

namespace DDT.Server.Deployments;

// Reason is a sentence for the operator or the technician. Message is the same sentence as a code for the web, if the
// web can see the decision. Field names the request member an Invalid refers to.
public sealed record DeploymentDecision(DeploymentOutcome Outcome, Deployment? Deployment, string? Reason, string? Field, ServerMessage? Message = null)
{
    // An Invalid decision about several fields at once, such as the answers to a run's inputs. The first one is also
    // Field.
    public IReadOnlyList<AnswerProblem> Problems { get; init; } = [];

    // Started means an accepted report started the run. InputsPending means it found the run waiting at its start for
    // these inputs.
    public bool Started { get; init; }

    public IReadOnlyList<AgentInput>? InputsPending { get; init; }

    public static DeploymentDecision Accepted(Deployment deployment) => new(DeploymentOutcome.Accepted, deployment, null, null);

    public static DeploymentDecision Unchanged(Deployment deployment) => new(DeploymentOutcome.Unchanged, deployment, null, null);

    public static DeploymentDecision NotFound(string reason) => new(DeploymentOutcome.NotFound, null, reason, null);

    public static DeploymentDecision NotFound(ServerMessage reason) => new(DeploymentOutcome.NotFound, null, reason.Text, null, reason);

    public static DeploymentDecision Conflict(string reason) => new(DeploymentOutcome.Conflict, null, reason, null);

    public static DeploymentDecision Conflict(ServerMessage reason) => new(DeploymentOutcome.Conflict, null, reason.Text, null, reason);

    public static DeploymentDecision Invalid(string field, string reason) => new(DeploymentOutcome.Invalid, null, reason, field);

    public static DeploymentDecision Invalid(string field, ServerMessage reason) => new(DeploymentOutcome.Invalid, null, reason.Text, field, reason);

    public static DeploymentDecision Refused(Deployment deployment, string reason) => new(DeploymentOutcome.Refused, deployment, reason, null);

    public static DeploymentDecision InvalidAnswers(IReadOnlyList<AnswerProblem> problems)
    {
        ArgumentNullException.ThrowIfNull(problems);
        ArgumentOutOfRangeException.ThrowIfZero(problems.Count);

        return new(DeploymentOutcome.Invalid, null, problems[0].Message.Text, problems[0].Field, problems[0].Message) { Problems = problems };
    }
}
