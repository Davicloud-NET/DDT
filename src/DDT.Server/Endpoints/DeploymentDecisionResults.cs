// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Deployments;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DDT.Server.Endpoints;

// The answers to a DeploymentDecision that refuses. The web gets the message codes; the agent logs the English titles.
internal static class DeploymentDecisionResults
{
    // Null for a decision the caller goes on with.
    public static int? RefusalStatus(DeploymentDecision decision) => decision.Outcome switch
    {
        DeploymentOutcome.NotFound => StatusCodes.Status404NotFound,
        DeploymentOutcome.Conflict => StatusCodes.Status409Conflict,
        _ => null,
    };

    // Every decision the web can get has a message; one without keeps its English alone.
    public static ProblemHttpResult WebProblem(DeploymentDecision decision, int statusCode) =>
        decision.Message is { } message
            ? ServerProblems.Problem(message, statusCode)
            : TypedResults.Problem(title: decision.Reason, statusCode: statusCode);

    // Every field problem of the decision with its code, such as one for each answer that cannot be taken.
    public static ValidationProblem WebInvalid(DeploymentDecision decision)
    {
        if (decision.Problems.Count == 0)
        {
            return decision.Message is { } invalid
                ? ServerProblems.Validation(decision.Field!, invalid)
                : TypedResults.ValidationProblem(new Dictionary<string, string[]> { [decision.Field!] = [decision.Reason!] });
        }

        FieldProblems problems = new();

        foreach (AnswerProblem problem in decision.Problems)
        {
            problems.Add(problem.Field, problem.Message);
        }

        return problems.ToResult();
    }

    public static ProblemHttpResult AgentProblem(DeploymentDecision decision, int statusCode) =>
        TypedResults.Problem(title: decision.Reason, statusCode: statusCode);

    // Every answer the console asks again, by its field, and all of them in the title, which the console shows.
    public static ValidationProblem AgentInvalid(DeploymentDecision decision) =>
        decision.Problems.Count == 0
            ? TypedResults.ValidationProblem(new Dictionary<string, string[]> { [decision.Field!] = [decision.Reason!] })
            : TypedResults.ValidationProblem(
                decision.Problems
                    .GroupBy(problem => problem.Field)
                    .ToDictionary(field => field.Key, field => field.Select(problem => problem.Message.Text).ToArray()),
                title: string.Join(" ", decision.Problems.Select(problem => problem.Message.Text)));
}
