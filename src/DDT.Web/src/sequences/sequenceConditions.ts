// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

// The server's ConditionOperator. Versions 1 and 2 know the first four; any other makes a document version 3, as an
// older agent's evaluator treats an operator it does not know as false.
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
  // Whether the machine has a value at all; value is not read.
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

// One name of the server's MachineVariableNames.Catalogue, as GET /api/sequences/facts lists them.
// changesDuringRun: the value can change while the run goes on, so a share's host cannot be made of it.
export interface FactView {
  name: string;
  type: FactType;
  changesDuringRun: boolean;
}

// The conditions of versions 1 and 2, kept beside when. variable is one of the server's MachineVariableNames, such
// as "Model", "MacAddress" or "Phase".
export interface StepCondition {
  variable: string;
  operator: ConditionOperator;
  value: string;
}

// A condition as a tree, as a step's when, an IF's test and a repeat's until are: groups whose parts must all, any or
// none hold, with tests at the leaves. An empty all or none holds, an empty any does not.
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
