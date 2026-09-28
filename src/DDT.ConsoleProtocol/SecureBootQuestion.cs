// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.ConsoleProtocol;

// Asked last, before the sequence writes a disk image this machine's Secure Boot would not start. As for EraseQuestion,
// the console sends what was typed as Text, and anything but Word erases nothing. SignedUnder is for UntrustedCa.
public sealed record SecureBootQuestion(
    string SequenceName,
    string? ImageName,
    SecureBootProblem Problem,
    MicrosoftUefiCas? SignedUnder,
    string Word) : ConsoleQuestion;
