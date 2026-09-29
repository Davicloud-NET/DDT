// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.ConsoleProtocol;

// Which task sequence to run, out of those this machine can run. The ones an assignment rule suggests come first.
// Answer with SequenceId. The agent then only asks what that sequence needs.
public sealed record SequenceQuestion(IReadOnlyList<SequenceOption> Sequences) : ConsoleQuestion;
