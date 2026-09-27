// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.ConsoleProtocol;

// A question for the person at the machine, which stays open until the console answers it with an AnswerMessage of the
// same Id or the agent withdraws it. The agent asks one question at a time, and never uses an Id twice.
public sealed record QuestionMessage(int Id, ConsoleQuestion Question) : ConsoleMessage;
