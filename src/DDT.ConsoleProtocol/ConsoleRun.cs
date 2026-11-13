// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization;

namespace DDT.ConsoleProtocol;

// A run of a task sequence. It has every step in order, the running step if there is one, and its percent when the
// step reports progress. Activity says what the agent does between steps. StepDetail is what the running step does now.
// Stopping: the server ended the run, and the agent waits for the running step to end. Both are left out while unset,
// so an older console reads the message as before.
public sealed record ConsoleRun(
    Guid Id,
    string SequenceName,
    IReadOnlyList<ConsoleStep> Steps,
    Guid? CurrentStepId,
    int? Percent,
    ConsoleActivity Activity,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? StepDetail = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Stopping = false);
