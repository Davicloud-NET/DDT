// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.ConsoleProtocol;

// A run of a task sequence: every step of the sequence in its order, the step that is running, if any, its percent
// where the step says how far it is, and what the agent does between steps.
public sealed record ConsoleRun(
    Guid Id,
    string SequenceName,
    IReadOnlyList<ConsoleStep> Steps,
    Guid? CurrentStepId,
    int? Percent,
    ConsoleActivity Activity);
