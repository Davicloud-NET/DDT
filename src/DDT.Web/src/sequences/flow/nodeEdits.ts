// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { ConditionNode } from "../sequenceConditions";
import type { SequenceDraft } from "../sequenceDraft";
import type { SequenceStep } from "../sequences";
import { changedCondition, conditionOf, type ConditionField } from "./conditionTree";
import type { FlowEdit, NodePatch } from "./flowEdits";
import { findNode } from "./flowTree";
import { replaceNode, withSteps } from "./treeChanges";

// The members of version 3 a node may be given although it has none yet.
function optionalMembers(node: SequenceStep): string[] {
  return [
    "when",
    "shares",
    ...(node.kind === "runScript" ? ["runAs"] : node.kind === "joinDomain" ? ["account"] : []),
  ];
}

const bodyFields: ReadonlySet<string> = new Set(["steps", "then", "else"]);

export function patchedNode(node: SequenceStep, patch: NodePatch): SequenceStep {
  const optional = optionalMembers(node);
  const fields = Object.entries(patch).filter(
    ([key]) =>
      key !== "id" &&
      key !== "kind" &&
      !bodyFields.has(key) &&
      (Object.hasOwn(node, key) || optional.includes(key)),
  );

  if (fields.length === 0) {
    return node;
  }

  const next: Record<string, unknown> = { ...node, ...Object.fromEntries(fields) };

  for (const [key, value] of fields) {
    if (optional.includes(key) && value === null) {
      Reflect.deleteProperty(next, key);
    }
  }

  return next as unknown as SequenceStep;
}

function withCondition(
  node: SequenceStep,
  field: ConditionField,
  tree: ConditionNode | null,
): SequenceStep {
  const always: ConditionNode = { kind: "all", parts: [] };

  switch (field) {
    case "when": {
      if (tree !== null) {
        return { ...node, when: tree };
      }

      const next = { ...node };
      delete next.when;

      return next;
    }
    case "test":
      return node.kind === "if" ? { ...node, test: tree ?? always } : node;
    case "until":
      return node.kind === "repeat" ? { ...node, until: tree ?? always } : node;
  }
}

export function conditionEdited(
  draft: SequenceDraft,
  edit: Extract<FlowEdit, { type: "editCondition" }>,
): SequenceDraft {
  const node = findNode(draft.steps, edit.id);
  const root = node === undefined ? undefined : conditionOf(node, edit.field);

  if (root === undefined) {
    return draft;
  }

  const tree = changedCondition(root, edit.path, edit.change);

  return tree === undefined
    ? draft
    : withSteps(
        draft,
        replaceNode(draft.steps, edit.id, (current) => withCondition(current, edit.field, tree)),
      );
}
