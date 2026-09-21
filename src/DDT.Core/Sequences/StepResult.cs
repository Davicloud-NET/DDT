// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Core.Sequences;

// Outputs become run variables that later steps see, and they survive restarts in the run's state.
public sealed record StepResult(StepOutcome Outcome, string? Error, IReadOnlyDictionary<string, string>? Outputs)
{
    public static StepResult Done(IReadOnlyDictionary<string, string>? outputs = null) => new(StepOutcome.Done, null, outputs);

    public static StepResult Failed(string error) => new(StepOutcome.Failed, error, null);

    // The step is done, and the machine has to restart before the next one.
    public static StepResult RebootRequired(IReadOnlyDictionary<string, string>? outputs = null) =>
        new(StepOutcome.RebootRequired, null, outputs);
}
