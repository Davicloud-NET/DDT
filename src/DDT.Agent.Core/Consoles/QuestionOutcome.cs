// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.ConsoleProtocol;

namespace DDT.Agent.Consoles;

// How a question ended: with the console's answer, without one, or Gone, when the console went away for good before it
// answered.
public readonly record struct QuestionOutcome(ConsoleAnswer? Answer, bool Gone)
{
    public static QuestionOutcome Unanswered => new(null, Gone: false);

    public static QuestionOutcome NoConsole => new(null, Gone: true);
}
