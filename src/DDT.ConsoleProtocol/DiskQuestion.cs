// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.ConsoleProtocol;

// Which disk the sequence erases, asked when the machine has more than one DDT can install on. Answer with DiskNumber,
// or with Back for the list of sequences.
public sealed record DiskQuestion(string SequenceName, IReadOnlyList<ConsoleDisk> Disks) : ConsoleQuestion;
