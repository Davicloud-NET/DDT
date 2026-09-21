// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;

namespace DDT.Core.Sequences;

// Variables are the run variables earlier steps output. Progress takes the step's percent.
public sealed record StepContext(
    Guid RunId,
    SequencePhase Phase,
    MachineVariables Machine,
    IReadOnlyDictionary<string, string> Variables,
    IProgress<int> Progress);
