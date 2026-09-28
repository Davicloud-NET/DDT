// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.ConsoleProtocol;

// An answer: Text for what was typed, SequenceId or DiskNumber for a choice, or Back to go back where the question
// allows it. Values answer the fields of an InputsQuestion, and Continue a PauseQuestion. Each question says which it
// takes.
public sealed record ConsoleAnswer(
    string? Text = null,
    Guid? SequenceId = null,
    int? DiskNumber = null,
    bool Back = false,
    IReadOnlyList<ConsoleInputValue>? Values = null,
    bool Continue = false);
