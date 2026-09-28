// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { ConditionGroupKind, ConditionNode, TestCondition } from "../sequenceConditions";
import type { SequenceStep } from "../sequences";

// A node's condition trees: its when, an IF's test and a repeat's until.
export type ConditionField = "when" | "test" | "until";

// A place in a condition tree: the indexes of the parts from its root, [] for the root itself.
export type ConditionPath = readonly number[];

// A change of a condition tree at a path.
export type ConditionChange =
  // null takes the node away: for the root, that leaves no when, and an IF's test or a repeat's until an empty all.
  | { op: "set"; node: ConditionNode | null }
  // At index or at the end. Added to a test, or where there is no when, it makes an all of what was there and part.
  | { op: "add"; part: ConditionNode; index?: number }
  | { op: "update"; patch: Partial<Omit<TestCondition, "kind">> }
  // As set with null.
  | { op: "remove" }
  // Makes the group there an all, an any or a none.
  | { op: "group"; kind: ConditionGroupKind }
  // Puts the node there into a new group of the kind.
  | { op: "wrap"; kind: ConditionGroupKind };

// The tree of a node's field, null where the node has none, undefined where the field is not the node's.
export function conditionOf(
  node: SequenceStep,
  field: ConditionField,
): ConditionNode | null | undefined {
  switch (field) {
    case "when":
      return node.when ?? null;
    case "test":
      return node.kind === "if" ? node.test : undefined;
    case "until":
      return node.kind === "repeat" ? node.until : undefined;
  }
}

// The field of a place in a tree as a problem and a run's evaluation name it, such as "when" or "test.parts[1]".
export function conditionPath(field: ConditionField, path: ConditionPath): string {
  return [field, ...path.map((index) => `parts[${String(index)}]`)].join(".");
}

export interface PlacedTest {
  path: number[];
  test: TestCondition;
}

// Every test of a tree with its path, in order.
export function testsOf(root: ConditionNode | null | undefined): PlacedTest[] {
  const tests: PlacedTest[] = [];

  const add = (node: ConditionNode, path: number[]) => {
    if (node.kind === "test") {
      tests.push({ path, test: node });
    } else {
      node.parts.forEach((part, index) => {
        add(part, [...path, index]);
      });
    }
  };

  if (root !== null && root !== undefined) {
    add(root, []);
  }

  return tests;
}

// Every test of a tree with its variable changed by rename; the same tree when none changed.
export function mapTests(
  root: ConditionNode,
  change: (test: TestCondition) => TestCondition,
): ConditionNode {
  if (root.kind === "test") {
    return change(root);
  }

  const parts = root.parts.map((part) => mapTests(part, change));

  return parts.every((part, index) => part === root.parts[index]) ? root : { ...root, parts };
}

function nodeAt(root: ConditionNode | null, path: ConditionPath): ConditionNode | null | undefined {
  let node: ConditionNode | null = root;

  for (const index of path) {
    if (node === null || node.kind === "test") {
      return undefined;
    }

    node = node.parts[index] ?? null;

    if (node === null) {
      return undefined;
    }
  }

  return node;
}

// Puts replacement at path, or takes the node there away when it is null. Undefined when the path leads nowhere.
function replaced(
  root: ConditionNode | null,
  path: ConditionPath,
  replacement: ConditionNode | null,
): ConditionNode | null | undefined {
  const [first, ...rest] = path;

  if (first === undefined) {
    return replacement;
  }

  if (root === null || root.kind === "test" || first < 0 || first >= root.parts.length) {
    return undefined;
  }

  const part = root.parts[first] ?? null;
  const inner = replaced(part, rest, replacement);

  if (inner === undefined) {
    return undefined;
  }

  return {
    ...root,
    parts:
      inner === null
        ? root.parts.filter((_, index) => index !== first)
        : root.parts.map((existing, index) => (index === first ? inner : existing)),
  };
}

// The tree after the change, null for none, undefined when the change does not fit the tree.
export function changedCondition(
  root: ConditionNode | null,
  path: ConditionPath,
  change: ConditionChange,
): ConditionNode | null | undefined {
  const target = nodeAt(root, path);

  if (target === undefined || (target === null && !(change.op === "add" && path.length === 0))) {
    return change.op === "set" && path.length === 0 ? change.node : undefined;
  }

  switch (change.op) {
    case "set":
      return replaced(root, path, change.node);
    case "remove":
      return replaced(root, path, null);
    case "add": {
      if (target === null) {
        return { kind: "all", parts: [change.part] };
      }

      if (target.kind === "test") {
        return replaced(root, path, { kind: "all", parts: [target, change.part] });
      }

      const at = Math.max(0, Math.min(change.index ?? target.parts.length, target.parts.length));

      return replaced(root, path, {
        ...target,
        parts: [...target.parts.slice(0, at), change.part, ...target.parts.slice(at)],
      });
    }
    case "update":
      return target?.kind === "test"
        ? replaced(root, path, { ...target, ...change.patch, kind: "test" })
        : undefined;
    case "group":
      return target !== null && target.kind !== "test"
        ? replaced(root, path, { kind: change.kind, parts: target.parts })
        : undefined;
    case "wrap":
      return target === null
        ? undefined
        : replaced(root, path, { kind: change.kind, parts: [target] });
  }
}
