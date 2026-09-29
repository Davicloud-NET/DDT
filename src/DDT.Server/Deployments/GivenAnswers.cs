// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Core.Values;

namespace DDT.Server.Deployments;

// The answers given with an assignment, an approval or a pick, and the values they make.
public sealed record GivenAnswers(
    // The answers to keep with the run. Account answers are left out, because RunCredentials keeps them.
    IReadOnlyList<RunAnswer> Answers,
    // The inputs answered, Account inputs included.
    IReadOnlyList<string> Answered,
    ValueResolution Values,
    IReadOnlyList<AnswerProblem> Problems);
