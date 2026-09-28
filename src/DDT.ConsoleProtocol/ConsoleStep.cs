// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.ConsoleProtocol;

// A node of a run, which lists every node of its tree in pre-order, containers included.
public sealed record ConsoleStep(
    Guid Id,
    string Name,
    // As a sequence's JSON names it, such as "partition", "runScript" or "repeat".
    string Kind,
    ConsolePhase Phase,
    ConsoleStepState State,
    string? Error,
    // The container the node sits in, null at the top, and how many containers deep it is.
    Guid? ParentId = null,
    int Depth = 0,
    // The times the node was entered, 0 for never.
    int Pass = 0,
    // A repeat's current time through its body, from 1.
    int Iteration = 0,
    ConsoleBranch? Branch = null);
