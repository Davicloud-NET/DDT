// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;

namespace DDT.Core.Sequences;

public sealed record StepContext(
    Guid RunId,
    SequencePhase Phase,
    // What conditions read in the step's phase. In a tree's run, Machine.Variables holds the run's values with
    // Variables on top. Templates such as a pause's message use them.
    MachineVariables Machine,
    // The run variables earlier steps output.
    IReadOnlyDictionary<string, string> Variables,
    IProgress<int> Progress);
