// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.ConsoleProtocol;

// Text for what was typed, SequenceId or DiskNumber for a choice, Values for an InputsQuestion's fields, Continue for a
// PauseQuestion, or Back where the question allows it. Each question says which it takes.
public sealed record ConsoleAnswer(
    string? Text = null,
    Guid? SequenceId = null,
    int? DiskNumber = null,
    bool Back = false,
    IReadOnlyList<ConsoleInputValue>? Values = null,
    bool Continue = false);
