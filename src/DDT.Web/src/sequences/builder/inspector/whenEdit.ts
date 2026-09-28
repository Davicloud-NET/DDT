// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { legacyTree } from "@/conditions/conditions";

import { changedCondition, type ConditionChange } from "../../flow/conditionTree";
import type { SequenceEdit } from "../../sequenceEdits";
import type { SequenceStep } from "../../sequences";

// Turns a change to a node's when into an edit. Version 1 and 2 conditions show up in one tree with the when, and
// they become the when at the first change. Null if the change doesn't fit that tree.
export function whenEdit(
  node: SequenceStep,
  path: readonly number[],
  condition: ConditionChange,
): SequenceEdit | null {
  if (node.conditions.length === 0) {
    return { type: "editCondition", id: node.id, field: "when", path, change: condition };
  }

  const tree = changedCondition(legacyTree(node.conditions, node.when), path, condition);

  if (tree === undefined) {
    return null;
  }

  return {
    type: "updateNode",
    id: node.id,
    patch: { conditions: [], when: tree },
    ...(condition.op !== "update" ? { chosen: true } : {}),
  };
}
