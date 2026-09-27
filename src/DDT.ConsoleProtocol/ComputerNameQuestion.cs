// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.ConsoleProtocol;

// The computer name, asked when the sequence joins a domain or names the machine in its cloud-init seed. Answer with
// Text, or with Back for the list of sequences. A name holds at most MaxLength of the letters A to Z, digits and hyphens,
// not only digits and not a hyphen first; the agent checks it, and Error says what was wrong with the last one.
public sealed record ComputerNameQuestion(string SequenceName, int MaxLength, string? Error) : ConsoleQuestion;
