// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;

namespace DDT.Server.Deployments;

// An answer to a run's input. Account inputs are the exception, because their answers are RunCredential rows.
// AnsweredBy is the user's name, and AtMachine says it was given at the machine rather than on the web. Stored in
// Deployment.Answers.
public sealed record RunAnswer(string Name, string Value, string? AnsweredBy, bool AtMachine, DateTimeOffset AnsweredUtc)
{
    public static IReadOnlyList<RunAnswer> Read(string? answers) =>
        answers is null ? [] : JsonSerializer.Deserialize(answers, DeploymentJsonContext.Default.IReadOnlyListRunAnswer) ?? [];

    public static string Write(IReadOnlyList<RunAnswer> answers) =>
        JsonSerializer.Serialize(answers, DeploymentJsonContext.Default.IReadOnlyListRunAnswer);
}
