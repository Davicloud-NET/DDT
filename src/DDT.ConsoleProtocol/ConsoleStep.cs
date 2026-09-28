// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.ConsoleProtocol;

// A step of a run. Kind is the step's kind as a sequence's JSON names it, such as "partition", "applyImage" or
// "runScript". Phase is where it runs. Error is why it failed. In a tree, a run lists every node in pre-order,
// containers ("group", "if", "repeat") included: ParentId is the container a node sits in, null at the top, and Depth
// counts containers from 0. Pass counts the times the node was entered, 0 for never; Iteration is a repeat's current
// time through its body, from 1; Branch is the path an IF took.
public sealed record ConsoleStep(
    Guid Id,
    string Name,
    string Kind,
    ConsolePhase Phase,
    ConsoleStepState State,
    string? Error,
    Guid? ParentId = null,
    int Depth = 0,
    int Pass = 0,
    int Iteration = 0,
    ConsoleBranch? Branch = null);
