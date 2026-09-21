// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Sequences;

// StepId is null for a problem of the whole sequence. Field is the camelCase JSON name within the step, such as
// "script" or "conditions[1].value", so an editor can show the problem at the field. Every problem keeps a sequence
// from running, so a warning, such as Windows steps without a local administrator, goes in a list of its own.
public sealed record SequenceProblem(Guid? StepId, string? Field, string Message);
