// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";

import { operatorText } from "@/conditions/conditionOperators";
import { testText, valueText } from "@/conditions/conditions";
import { subjectFor, subjectsOf, type Subject } from "@/conditions/conditionSubjects";
import { factCatalogue } from "@/conditions/factCatalogue";
import type { DeploymentStepView } from "@/deployments/deployments";
import { conditionOf, conditionPath, testsOf } from "@/sequences/flow/conditionTree";
import type { TestCondition } from "@/sequences/sequenceConditions";
import type { SequenceDefinition, SequenceStep } from "@/sequences/sequences";
import { operatorTakesValue } from "@/sequences/steps";
import type { ResolvedValue } from "@/values/values";

// Why a run took its path, from the tests the agent recorded when it decided, never from what the machine reports
// now. The tests read as the flow builder writes them.

// What a run's conditions can name: the machine's facts, the run's own values, the values of rules and machine roles,
// and the sequence's variables and inputs.
export function runSubjects(
  definition: SequenceDefinition | null,
  values: readonly ResolvedValue[] = [],
): Subject[] {
  return subjectsOf({
    facts: factCatalogue,
    valueNames: values.map((value) => value.name),
    variables: definition?.variables ?? [],
    inputs: definition?.inputs ?? [],
  });
}

// A test's parts as a sentence shows them: what it tests, how, and the value; null for a test of whether there is a
// value at all.
export interface TestWords {
  subject: string;
  operator: string;
  value: string | null;
}

export function testWords(test: TestCondition, subjects: readonly Subject[]): TestWords {
  const subject = subjectFor(subjects, test.variable);

  return {
    subject: subject.label,
    operator: operatorText(test.operator, subject.kind),
    value: operatorTakesValue(test.operator) ? valueText(subject, test.value) : null,
  };
}

// One test as the run decided it.
export interface TestOutcome {
  path: string;
  // The test, null where the path names nothing in the node, as for a node changed since.
  test: TestCondition | null;
  held: boolean;
  actual: string | null;
}

// Every test of a node by the path a problem and an evaluation name it with: conditions[0], when, test.parts[1].
export function testsByPath(node: SequenceStep): Map<string, TestCondition> {
  const found = new Map<string, TestCondition>();

  node.conditions.forEach((condition, index) => {
    found.set(`conditions[${String(index)}]`, { kind: "test", ...condition });
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

function actualText(outcome: TestOutcome, subjects: readonly Subject[]): string | null {
  return outcome.actual === null || outcome.test === null
    ? outcome.actual
    : valueText(subjectFor(subjects, outcome.test.variable), outcome.actual);
}

// What an outcome's value was, as a line under its test: what the machine reported, or the value of the run.
export function reportedText(outcome: TestOutcome, subjects: readonly Subject[]): string {
  const machine =
    outcome.test !== null && subjectFor(subjects, outcome.test.variable).section === "machine";
  const actual = actualText(outcome, subjects);

  if (actual === null) {
    return machine ? t`The machine reported no value.` : t`There was no value.`;
  }

  return machine ? t`The machine reported ${actual}.` : t`The value was ${actual}.`;
}

// An outcome as a clause of a decision's line, such as Model contains Latitude holds for "Latitude 7450".
export function outcomeText(outcome: TestOutcome, subjects: readonly Subject[]): string {
  const test = outcome.test === null ? outcome.path : testText(outcome.test, subjects);
  const actual = actualText(outcome, subjects);

  if (actual === null) {
    return outcome.held ? t`${test} holds, with no value` : t`${test} does not hold, with no value`;
  }

  return outcome.held ? t`${test} holds for "${actual}"` : t`${test} does not hold for "${actual}"`;
}

function clauses(outcomes: readonly TestOutcome[], subjects: readonly Subject[]): string {
  return outcomes.map((outcome) => outcomeText(outcome, subjects)).join("; ");
}

// A step's decision in the list of steps: the branch an IF took, why a node was skipped or when a repeat stopped, with
// its tests. Null where the node decided nothing, or the agent recorded no tests, as older agents do not.
export function decisionLine(
  node: SequenceStep,
  step: DeploymentStepView | null,
  subjects: readonly Subject[],
): string | null {
  if (step === null) {
    return null;
  }

  if (step.state === "Skipped") {
    const outcomes = outcomesOf(node, step, "condition");

    if (outcomes.length === 0) {
      return null;
    }

    const reasons = clauses(outcomes, subjects);

    return t`Skipped: ${reasons}`;
  }

  if (node.kind === "if" && step.branch !== null && step.branch !== undefined) {
    const outcomes = outcomesOf(node, step, "test");
    const reasons = clauses(outcomes, subjects);

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

    const reasons = clauses(outcomes, subjects);

    return outcomes.every((outcome) => outcome.held)
      ? t`Stopped after ${times} of at most ${most} times: ${reasons}`
      : t`Stopped at the limit of ${most} times: ${reasons}`;
  }

  return null;
}

// The heading of a node's decision in its details: which branch it took, why it was skipped, or how far it went.
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
    return repeatText(node.maxTimes, step);
  }

  return null;
}

// How far a repeat went: the time through its body it is on, or how many it took.
export function repeatText(most: number, step: DeploymentStepView): string {
  const times = step.iteration ?? 0;

  return step.state === "Running"
    ? t`Iteration ${times} of at most ${most}`
    : t`Done after ${times} of at most ${most} times`;
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
