// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Sequences;

// One name from MachineVariableNames.Catalogue, as GET /api/sequences/facts lists it for the condition builder.
// ChangesDuringRun means the value can change during the run, so a share's host can't be built from it.
public sealed record FactView(string Name, FactType Type, bool ChangesDuringRun);
