// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { DropOperation, DropPosition, DropTarget, Key } from "react-aria-components";

import { nodeTitle } from "../../flow/flowLabels";
import { isWithin, type BodyName, type Slot, type TreeIndex } from "../../flow/flowTree";
import { NODE_TYPE } from "../flowDrag";
import { BRANCH_SEPARATOR } from "./outlineRows";

// The container and body a row holds its nodes in: a container's body, or the branch of a Then or an Else row.
function bodyOf(row: { key: string; index: TreeIndex }): { parent: string; body: BodyName } | null {
  const [id = "", branch] = row.key.split(BRANCH_SEPARATOR);
  const node = row.index.byId.get(id)?.node;

  if (node === undefined) {
    return null;
  }

  if (branch === "then" || branch === "else") {
    return { parent: id, body: branch };
  }

  return node.kind === "group" || node.kind === "repeat"
    ? { parent: id, body: "steps" }
    : node.kind === "if"
      ? { parent: id, body: "then" }
      : null;
}

// The gap a drop on a row means: before or after the node, or at the end of what the row holds.
function slotOfDrop(index: TreeIndex, key: string, position: DropPosition): Slot | null {
  if (position === "on") {
    const body = bodyOf({ key, index });

    if (body === null) {
      return null;
    }

    const container = index.byId.get(body.parent)?.node;
    const list =
      container === undefined
        ? []
        : container.kind === "if"
          ? container[body.body === "else" ? "else" : "then"]
          : container.kind === "group" || container.kind === "repeat"
            ? container.steps
            : [];

    return { parent: body.parent, body: body.body, index: list.length };
  }

  if (key.includes(BRANCH_SEPARATOR)) {
    return null;
  }

  const entry = index.byId.get(key);

  return entry === undefined
    ? null
    : {
        parent: entry.parent,
        body: entry.body,
        index: position === "before" ? entry.index : entry.index + 1,
      };
}

// The nodes among the dragged rows. A Then or an Else stays with its IF.
function nodeKeys(keys: Iterable<Key>): string[] {
  return [...keys].map(String).filter((key) => !key.includes(BRANCH_SEPARATOR));
}

export function outlineDragItems(index: TreeIndex, keys: Iterable<Key>) {
  return nodeKeys(keys).map((key) => {
    const node = index.byId.get(key)?.node;

    return { [NODE_TYPE]: key, "text/plain": node === undefined ? key : nodeTitle(node) };
  });
}

export function outlineDropOperation(
  index: TreeIndex,
  target: DropTarget,
  allowed: readonly DropOperation[],
): DropOperation {
  if (target.type !== "item" || !allowed.includes("move")) {
    return "cancel";
  }

  const slot = slotOfDrop(index, String(target.key), target.dropPosition);

  return slot === null ? "cancel" : "move";
}

// The nodes a drop moves and where to, or null if the move would put a node inside itself.
export function outlineMove(
  index: TreeIndex,
  keys: Iterable<Key>,
  target: { key: Key; dropPosition: DropPosition },
): { ids: string[]; slot: Slot } | null {
  const ids = nodeKeys(keys);
  const slot = slotOfDrop(index, String(target.key), target.dropPosition);

  return slot !== null &&
    ids.length > 0 &&
    !ids.some((id) => slot.parent !== null && isWithin(index, slot.parent, id))
    ? { ids, slot }
    : null;
}
