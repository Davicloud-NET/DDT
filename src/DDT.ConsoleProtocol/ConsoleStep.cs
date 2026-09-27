// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.ConsoleProtocol;

// A step of a run. Kind is the step's kind as a sequence's JSON names it, such as "partition", "applyImage" or
// "runScript". Phase is where it runs. Error is why it failed.
public sealed record ConsoleStep(Guid Id, string Name, string Kind, ConsolePhase Phase, ConsoleStepState State, string? Error);
