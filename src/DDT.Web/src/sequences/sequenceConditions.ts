// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

// The server's ConditionOperator. Versions 1 and 2 know the first four. Any other makes a document version 3, because
// an older agent's evaluator treats an unknown operator as false.
export type ConditionOperator =
  | "Equals"
  | "NotEquals"
  | "StartsWith"
  | "Contains"
  | "NotContains"
  | "EndsWith"
  // * stands for any text and ? for one character.
  | "Matches"
  // value is a list separated by semicolons.
  | "In"
  // Whether the machine has a value at all. value isn't read.
  | "Exists"
  | "NotExists"
  // Compared as numbers.
  | "Greater"
  | "GreaterOrEqual"
  | "Less"
  | "LessOrEqual"
  // An IPv4 address within a network written as 10.0.0.0/24.
  | "InSubnet";

// What kind of value a fact holds, which decides the operators that fit it. A YesNo value is "true" or "false".
export type FactType = "Text" | "Number" | "YesNo" | "IPv4" | "Mac";

// One name from the server's MachineVariableNames.Catalogue, as GET /api/sequences/facts lists them.
// changesDuringRun means the value can change during the run, so a share's host can't be built from it.
export interface FactView {
  name: string;
  type: FactType;
  changesDuringRun: boolean;
}

// Version 1 and 2 conditions, kept next to when. variable is one of the server's MachineVariableNames, such as
// "Model", "MacAddress" or "Phase".
export interface StepCondition {
  variable: string;
  operator: ConditionOperator;
  value: string;
}

// A condition as a tree, used for a step's when, an IF's test and a Repeat's until. Groups say whether all, any or
// none of their parts must hold, and tests sit at the leaves. An empty all or none holds. An empty any doesn't.
export interface TestCondition {
  kind: "test";
  // A fact, a run variable, or a value the sequence declares or rules and machine roles set.
  variable: string;
  operator: ConditionOperator;
  value: string;
}

export type ConditionGroupKind = "all" | "any" | "none";

export interface ConditionGroup {
  kind: ConditionGroupKind;
  parts: ConditionNode[];
}

export type ConditionNode = ConditionGroup | TestCondition;
