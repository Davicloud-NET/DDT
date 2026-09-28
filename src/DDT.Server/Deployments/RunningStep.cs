// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;

namespace DDT.Server.Deployments;

// A step the machine's active run is running, as frozen with the run. Step is null when the run's tree has no such node.
public sealed record RunningStep(Deployment Run, SequenceStep? Step, RunInputs Inputs, SequenceDefinition Definition);
