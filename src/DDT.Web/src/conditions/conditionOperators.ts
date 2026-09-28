// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";

import type { ConditionOperator } from "@/sequences/sequenceConditions";
import { operatorLabel } from "@/sequences/steps";

import type { ValueKind } from "./conditionSubjects";

// The operators the server's ConditionChecks takes for the fact type behind each kind: memory is a Number, a network
// or a list of choices Text. A value from a rule, a role or the sequence has no type: text, or one of its choices.
const operatorsByKind: Record<ValueKind, readonly ConditionOperator[]> = {
  text: [
    "Equals",
    "NotEquals",
    "Contains",
    "NotContains",
    "StartsWith",
    "EndsWith",
    "Matches",
    "In",
    "Exists",
    "NotExists",
  ],
  oneOf: [
    "Equals",
    "NotEquals",
    "In",
    "Contains",
    "NotContains",
    "StartsWith",
    "EndsWith",
    "Matches",
    "Exists",
    "NotExists",
  ],
  number: [
    "Equals",
    "NotEquals",
    "GreaterOrEqual",
    "Greater",
    "LessOrEqual",
    "Less",
    "In",
    "Exists",
    "NotExists",
  ],
  memory: [
    "GreaterOrEqual",
    "Greater",
    "LessOrEqual",
    "Less",
    "Equals",
    "NotEquals",
    "In",
    "Exists",
    "NotExists",
  ],
  yesNo: ["Equals", "NotEquals", "Exists", "NotExists"],
  ipv4: ["InSubnet", "Equals", "NotEquals", "StartsWith", "Matches", "In", "Exists", "NotExists"],
  network: [
    "Equals",
    "NotEquals",
    "Contains",
    "NotContains",
    "StartsWith",
    "EndsWith",
    "Matches",
    "In",
    "Exists",
    "NotExists",
  ],
  // Contains is the match on whole bytes the MAC conditions of versions 1 and 2 have.
  mac: [
    "Equals",
    "NotEquals",
    "StartsWith",
    "EndsWith",
    "Contains",
    "NotContains",
    "In",
    "Exists",
    "NotExists",
  ],
};

// The operators that fit a kind of value, the one a new condition takes first.
export function operatorsFor(kind: ValueKind): readonly ConditionOperator[] {
  return operatorsByKind[kind];
}

// What an operator says for a kind: Equals and NotEquals read "is" and "is not" for yes or no and for a list's choices.
export function operatorText(operator: ConditionOperator, kind: ValueKind): string {
  if (kind === "yesNo" && operator === "Equals") {
    return t`is`;
  }

  if (kind === "yesNo" && operator === "NotEquals") {
    return t`is not`;
  }

  if (kind === "oneOf" && operator === "Equals") {
    return t`is`;
  }

  if (kind === "oneOf" && operator === "NotEquals") {
    return t`is not`;
  }

  return operatorLabel(operator);
}
