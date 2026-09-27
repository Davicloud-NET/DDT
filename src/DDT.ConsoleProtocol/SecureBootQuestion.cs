// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.ConsoleProtocol;

// The Secure Boot override, asked last on a machine with Secure Boot on before the sequence writes a disk image that
// machine would not start: ImageName is the image, Problem why it would not start, and SignedUnder the CAs its boot file
// is signed under, for UntrustedCa. As for EraseQuestion, the person types Word, which is ANYWAY, and the console sends
// what was typed as Text. Anything but the word, or Back, goes back to the list of sequences and erases nothing.
public sealed record SecureBootQuestion(
    string SequenceName,
    string? ImageName,
    SecureBootProblem Problem,
    MicrosoftUefiCas? SignedUnder,
    string Word) : ConsoleQuestion;
