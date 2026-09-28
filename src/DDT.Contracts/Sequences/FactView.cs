// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Sequences;

// One name of MachineVariableNames.Catalogue, as GET /api/sequences/facts lists them for the condition builder.
// ChangesDuringRun: the value can change while the run goes on, so a share's host cannot be made of it.
public sealed record FactView(string Name, FactType Type, bool ChangesDuringRun);
