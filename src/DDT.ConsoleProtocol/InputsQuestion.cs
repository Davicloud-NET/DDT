// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.ConsoleProtocol;

// The sequence's inputs, all on one page, after the pick or while a run waits at its start. Answer with Values, or Back
// after the pick. Error is about the answers as a whole, such as the server refusing them. Answers are never logged.
public sealed record InputsQuestion(string SequenceName, IReadOnlyList<ConsoleInput> Inputs, string? Error) : ConsoleQuestion;
