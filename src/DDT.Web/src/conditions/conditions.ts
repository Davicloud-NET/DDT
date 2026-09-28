// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { plural, t } from "@lingui/core/macro";

import { formattingLocale } from "@/i18n/i18n";
import type {
  ConditionGroupKind,
  ConditionNode,
  StepCondition,
  TestCondition,
} from "@/sequences/sequenceConditions";
import { operatorTakesValue } from "@/sequences/steps";

import { operatorText } from "./conditionOperators";
import { sameName, subjectFor, type Subject } from "./conditionSubjects";
import { gigabytesOf, listOf } from "./conditionValues";

// The words a condition reads as, in the flow builder and in the rules.

// A value as a sentence says it: yes or no, memory in GB, the label of a choice, a list joined.
export function valueText(subject: Subject, value: string): string {
  const one = (item: string) => {
    switch (subject.kind) {
      case "yesNo": {
        const lower = item.trim().toLowerCase();

        return ["true", "yes", "1"].includes(lower)
          ? t`yes`
          : ["false", "no", "0"].includes(lower)
            ? t`no`
            : item;
      }
      case "memory": {
        const size = gigabytesOf(item);

        return t`${size} GB`;
      }
      case "oneOf":
        return subject.choices.find((choice) => sameName(choice.value, item))?.label ?? item;
      default:
        return item;
    }
  };
  const items = listOf(value);

  return items.length > 1
    ? new Intl.ListFormat(formattingLocale(), { type: "disjunction" }).format(items.map(one))
    : one(value.trim());
}

export function testText(test: TestCondition, subjects: readonly Subject[]): string {
  const subject = subjectFor(subjects, test.variable);
  const label = subject.label;
  const operator = operatorText(test.operator, subject.kind);

  if (!operatorTakesValue(test.operator)) {
    return t`${label} ${operator}`;
  }

  const value = valueText(subject, test.value);

  return t`${label} ${operator} ${value}`;
}

// A condition in words, such as "Model contains Latitude and Memory is at least 8 GB". Groups inside groups are in
// brackets.
export function conditionText(node: ConditionNode, subjects: readonly Subject[]): string {
  if (node.kind === "test") {
    return testText(node, subjects);
  }

  const parts = node.parts.map((part) =>
    part.kind === "test" || part.parts.length <= 1
      ? conditionText(part, subjects)
      : `(${conditionText(part, subjects)})`,
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

function testCount(node: ConditionNode | null): number {
  return node === null
    ? 0
    : node.kind === "test"
      ? 1
      : node.parts.reduce((sum, part) => sum + testCount(part), 0);
}

// A condition short enough for a node's card: its one test in words, or how many tests it has.
export function conditionSummary(node: ConditionNode | null, subjects: readonly Subject[]): string {
  const count = testCount(node);

  if (node === null || count === 0) {
    return t`Always`;
  }

  return count === 1 || node.kind === "test"
    ? conditionText(node, subjects)
    : plural(count, { one: "# condition", other: "# conditions" });
}

export type ConditionUse = "when" | "test" | "until" | "rule";

// What a condition means where it is, as the builder says it under its rows.
export function conditionSentence(
  use: ConditionUse,
  node: ConditionNode | null,
  subjects: readonly Subject[],
): string {
  const empty = node === null || testCount(node) === 0;
  const condition = empty ? "" : conditionText(node, subjects);

  switch (use) {
    case "when":
      return empty ? t`Runs on every machine.` : t`Runs only where ${condition}.`;
    case "test":
      return empty
        ? t`Every machine goes along Then.`
        : t`Machines where ${condition} go along Then.`;
    case "until":
      return empty ? t`Stops after the first time.` : t`Stops repeating once ${condition}.`;
    case "rule":
      return empty ? t`Matches every machine.` : t`Matches machines where ${condition}.`;
  }
}

// How deep a condition may go: a group within a group, four levels in all.
export const MAX_CONDITION_DEPTH = 4;

export const groupKinds: readonly ConditionGroupKind[] = ["all", "any", "none"];

// A group as its choice in the builder says it.
export function groupLabel(kind: ConditionGroupKind): string {
  switch (kind) {
    case "all":
      return t`All of these hold`;
    case "any":
      return t`At least one of these holds`;
    case "none":
      return t`None of these hold`;
  }
}

// A step's conditions of versions 1 and 2 as a tree, with its when beside them, so the builder shows both as one.
export function legacyTree(
  conditions: readonly StepCondition[],
  when: ConditionNode | null | undefined,
): ConditionNode | null {
  const tests: ConditionNode[] = conditions.map((condition) => ({ kind: "test", ...condition }));

  if (tests.length === 0) {
    return when ?? null;
  }

  return { kind: "all", parts: when === null || when === undefined ? tests : [...tests, when] };
}

// Where a place of the tree legacyTree made is in the step: its conditions for the tests that came from them, its
// when for the rest, such as "conditions[1]" or "when.parts[0]".
export function legacyPath(legacyCount: number, hasWhen: boolean, path: readonly number[]): string {
  const parts = (from: readonly number[]) =>
    from.map((index) => `.parts[${String(index)}]`).join("");

  if (legacyCount === 0) {
    return `when${parts(path)}`;
  }

  const [first, ...rest] = path;

  if (first === undefined) {
    return "conditions";
  }

  if (first < legacyCount) {
    return `conditions[${String(first)}]`;
  }

  return hasWhen ? `when${parts(rest)}` : "conditions";
}
