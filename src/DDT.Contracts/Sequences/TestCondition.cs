// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Sequences;

// Variable names a fact from MachineVariableNames.Catalogue, a run variable, or a value the sequence declares or rules
// and machine roles set. Value is not read for Exists and NotExists.
public sealed record TestCondition(string Variable, ConditionOperator Operator, string Value = "") : ConditionNode;
