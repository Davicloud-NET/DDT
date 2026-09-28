// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";

import type { DeploymentStepView } from "@/deployments/deployments";
import { formattingLocale } from "@/i18n/i18n";
import { factLabel, factValueText, isFact, isRunVariable } from "@/machines/facts";
import { conditionOf, testsOf, conditionPath } from "@/sequences/flow/conditionTree";
import type {
  ConditionNode,
  ConditionOperator,
  SequenceStep,
  TestCondition,
} from "@/sequences/sequences";
import { operatorLabel, operatorTakesValue } from "@/sequences/steps";

// Why a run took the path it took, from what the agent recorded when it decided: the tests of an IF, of a node's
// condition and of a repeat's end, each with whether it held and the value it was tested against. What the machine
// reports now does not enter into it.

// Facts whose values are numbers or yes and no, which a sentence does not put in quotes.
const plainFacts: ReadonlySet<string> = new Set(
  [
    "MemoryMegabytes",
    "ProcessorCores",
    "LogicalProcessors",
    "TpmPresent",
    "TpmVersion",
    "SecureBootCapable",
    "SecureBootEnabled",
    "IPv4PrefixLength",
    "LastStepFailed",
    "LastExitCode",
  ].map((name) => name.toLowerCase()),
);

const yesNoFacts: ReadonlySet<string> = new Set(
  ["TpmPresent", "SecureBootCapable", "SecureBootEnabled", "LastStepFailed"].map((name) =>
    name.toLowerCase(),
  ),
);

// A test's parts as a sentence shows them: what it tests, how, and the value, in the person's language.
export interface TestWords {
  subject: string;
  operator: string;
  // Null for a test of whether there is a value at all.
  value: string | null;
}

function operatorWords(variable: string, operator: ConditionOperator): string {
  if (yesNoFacts.has(variable.toLowerCase())) {
    if (operator === "Equals") {
      return t`is`;
    }

    if (operator === "NotEquals") {
      return t`is not`;
    }
  }

  return operatorLabel(operator);
}

function valueWords(variable: string, value: string): string {
  const shown = isFact(variable) ? factValueText(variable, value) : value;

  return plainFacts.has(variable.toLowerCase()) ? shown : `"${shown}"`;
}

export function testWords(test: Pick<TestCondition, "variable" | "operator" | "value">): TestWords {
  return {
    subject: factLabel(test.variable),
    operator: operatorWords(test.variable, test.operator),
    value: operatorTakesValue(test.operator) ? valueWords(test.variable, test.value) : null,
  };
}

// A test as one sentence, such as Model contains "Latitude".
export function testText(test: Pick<TestCondition, "variable" | "operator" | "value">): string {
  const { subject, operator, value } = testWords(test);

  return value === null ? t`${subject} ${operator}` : t`${subject} ${operator} ${value}`;
}

// A condition in words: its tests joined with and or or, a none as not.
export function conditionText(node: ConditionNode): string {
  if (node.kind === "test") {
    return testText(node);
  }

  const parts = node.parts.map((part) =>
    part.kind === "test" || part.parts.length <= 1
      ? conditionText(part)
      : `(${conditionText(part)})`,
  );
  const locale = formattingLocale();

  if (parts.length === 0) {
    return node.kind === "any" ? t`nothing` : t`always`;
  }

  if (node.kind === "all") {
    return new Intl.ListFormat(locale, { type: "conjunction" }).format(parts);
  }

  const either = new Intl.ListFormat(locale, { type: "disjunction" }).format(parts);

  return node.kind === "any" ? either : t`not (${either})`;
}

// One test as the run decided it.
export interface TestOutcome {
  path: string;
  // The test, null where the path names nothing in the node, as for a node changed since.
  test: Pick<TestCondition, "variable" | "operator" | "value"> | null;
  held: boolean;
  actual: string | null;
}

// Every test of a node by the path a problem and an evaluation name it with: conditions[0], when, test.parts[1].
export function testsByPath(
  node: SequenceStep,
): Map<string, Pick<TestCondition, "variable" | "operator" | "value">> {
  const found = new Map<string, Pick<TestCondition, "variable" | "operator" | "value">>();

  node.conditions.forEach((condition, index) => {
    found.set(`conditions[${String(index)}]`, condition);
  });

  for (const field of ["when", "test", "until"] as const) {
    for (const placed of testsOf(conditionOf(node, field))) {
      found.set(conditionPath(field, placed.path), placed.test);
    }
  }

  return found;
}

// The tests the run recorded for a node, of one field or of all: "test" for an IF's test, "until" for a repeat's end,
// "condition" for the node's own conditions and when.
export function outcomesOf(
  node: SequenceStep,
  step: DeploymentStepView | null,
  field: "test" | "until" | "condition" | "all" = "all",
): TestOutcome[] {
  const tests = testsByPath(node);

  return (step?.evaluation ?? [])
    .filter((evaluation) => {
      const root = evaluation.path.split(/[.[]/)[0] ?? "";

      switch (field) {
        case "all":
          return true;
        case "condition":
          return root === "conditions" || root === "when";
        default:
          return root === field;
      }
    })
    .map((evaluation) => ({
      path: evaluation.path,
      test: tests.get(evaluation.path) ?? null,
      held: evaluation.held,
      actual: evaluation.actual,
    }));
}

// What an outcome's value was, as a line under its test.
export function reportedText(outcome: TestOutcome): string {
  const variable = outcome.test?.variable ?? "";
  const machine = isFact(variable) && !isRunVariable(variable);
  const actual =
    outcome.actual === null
      ? null
      : isFact(variable)
        ? factValueText(variable, outcome.actual)
        : outcome.actual;

  if (actual === null) {
    return machine ? t`The machine reported no value.` : t`There was no value.`;
  }

  return machine ? t`The machine reported ${actual}.` : t`The value was ${actual}.`;
}

// An outcome as a clause of a decision's line, such as Model contains "Latitude" holds for "Latitude 7450".
export function outcomeText(outcome: TestOutcome): string {
  const test = outcome.test === null ? outcome.path : testText(outcome.test);
  const variable = outcome.test?.variable ?? "";
  const actual =
    outcome.actual === null
      ? null
      : isFact(variable)
        ? factValueText(variable, outcome.actual)
        : outcome.actual;

  if (actual === null) {
    return outcome.held ? t`${test} holds, with no value` : t`${test} does not hold, with no value`;
  }

  return outcome.held ? t`${test} holds for "${actual}"` : t`${test} does not hold for "${actual}"`;
}

function clauses(outcomes: readonly TestOutcome[]): string {
  return outcomes.map(outcomeText).join("; ");
}

// The decision a node's step shows in the list of the run's steps: the branch an IF took, why a node was skipped,
// or when a repeat stopped, with the tests that decided it. Null where the node decided nothing, or where the agent
// recorded no tests, as older agents do not.
export function decisionLine(node: SequenceStep, step: DeploymentStepView | null): string | null {
  if (step === null) {
    return null;
  }

  if (step.state === "Skipped") {
    const outcomes = outcomesOf(node, step, "condition");

    if (outcomes.length === 0) {
      return null;
    }

    const reasons = clauses(outcomes);

    return t`Skipped: ${reasons}`;
  }

  if (node.kind === "if" && step.branch !== null && step.branch !== undefined) {
    const outcomes = outcomesOf(node, step, "test");
    const reasons = clauses(outcomes);

    if (outcomes.length === 0) {
      return step.branch === "Then" ? t`Took Then` : t`Took Else`;
    }

    return step.branch === "Then" ? t`Took Then: ${reasons}` : t`Took Else: ${reasons}`;
  }

  if (node.kind === "repeat" && (step.state === "Done" || step.state === "Failed")) {
    const outcomes = outcomesOf(node, step, "until");
    const times = step.iteration ?? 0;
    const most = node.maxTimes;

    if (outcomes.length === 0 || times === 0) {
      return null;
    }

    const reasons = clauses(outcomes);

    return outcomes.every((outcome) => outcome.held)
      ? t`Stopped after ${times} of at most ${most} times: ${reasons}`
      : t`Stopped at the limit of ${most} times: ${reasons}`;
  }

  return null;
}

// The heading of a node's decision in its details: which branch it took, why it was skipped, or when it stopped.
export function decisionTitle(node: SequenceStep, step: DeploymentStepView | null): string | null {
  if (step === null) {
    return null;
  }

  if (step.state === "Skipped") {
    return t`Skipped, because its condition did not hold`;
  }

  if (node.kind === "if" && step.branch !== null && step.branch !== undefined) {
    return step.branch === "Then" ? t`Took Then` : t`Took Else`;
  }

  if (node.kind === "repeat" && step.iteration !== undefined && step.iteration > 0) {
    const times = step.iteration;
    const most = node.maxTimes;

    return step.state === "Running"
      ? t`Iteration ${times} of at most ${most}`
      : t`Done after ${times} of at most ${most} times`;
  }

  return null;
}

// The outcomes a node's details show under its decision.
export function decisionOutcomes(
  node: SequenceStep,
  step: DeploymentStepView | null,
): TestOutcome[] {
  if (step === null) {
    return [];
  }

  if (step.state === "Skipped") {
    return outcomesOf(node, step, "condition");
  }

  if (node.kind === "if") {
    return outcomesOf(node, step, "test");
  }

  if (node.kind === "repeat") {
    return outcomesOf(node, step, "until");
  }

  return [];
}
