// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Sequences;

// Variable is one of MachineVariableNames.All. It is a string rather than an enum so later versions can add names
// without breaking older readers.
public sealed record StepCondition(string Variable, ConditionOperator Operator, string Value);
