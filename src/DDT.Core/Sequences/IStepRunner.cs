// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;

namespace DDT.Core.Sequences;

// One runner for every leaf kind: the agent dispatches by kind and fails a kind it cannot run, so a new kind needs no
// change to the engine. Containers and Set variable never reach it; the engine does them itself. A step that ends in an
// exception fails. Once a stop is requested, an exception or a Failed result counts as the stop, so a runner need not
// tell the stop apart from its own timeout.
public interface IStepRunner
{
    Task<StepResult> RunAsync(SequenceStep step, StepContext context, CancellationToken cancellationToken);
}
