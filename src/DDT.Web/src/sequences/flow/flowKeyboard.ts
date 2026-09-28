// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { SequenceStep } from "../sequences";
import { isContainer } from "../steps";
import type { FlowEdit } from "./flowEdits";
import { bodiesOf, type Slot, type TreeEntry, type TreeIndex } from "./flowTree";

// The keys of the flow canvas, from the tree alone: one Tab stop, Up and Down the way the flow runs, Left and Right
// across an IF's branches, Home and End to the ends, and Escape out to the container around.

export type FlowMove = "up" | "down" | "left" | "right" | "home" | "end" | "parent";

export type FlowCommand =
  | { type: "move"; move: FlowMove }
  // Enter: to the node's fields in the inspector.
  | { type: "open" }
  | { type: "remove" }
  | { type: "copy" }
  | { type: "cut" }
  | { type: "paste" }
  | { type: "duplicate" }
  // Alt with Up or Down: the node trades places with the one before or after it.
  | { type: "shift"; by: -1 | 1 }
  // Shift+F10 or the menu key: the node's menu.
  | { type: "menu" };

interface KeyInput {
  key: string;
  ctrlKey: boolean;
  metaKey: boolean;
  shiftKey: boolean;
  altKey: boolean;
}

// The command a key gives on a node of the canvas, or null for a key the canvas leaves alone, such as Ctrl+Z, which
// the page's history takes, and the zoom keys, which the canvas's viewport takes.
export function flowCommand(event: KeyInput): FlowCommand | null {
  const command = event.ctrlKey || event.metaKey;

  if (command && !event.altKey && !event.shiftKey) {
    switch (event.key.toLowerCase()) {
      case "c":
        return { type: "copy" };
      case "x":
        return { type: "cut" };
      case "v":
        return { type: "paste" };
      case "d":
        return { type: "duplicate" };
      default:
        return null;
    }
  }

  if (command) {
    return null;
  }

  if (event.key === "ContextMenu" || (event.shiftKey && event.key === "F10")) {
    return { type: "menu" };
  }

  if (event.altKey) {
    return event.key === "ArrowUp"
      ? { type: "shift", by: -1 }
      : event.key === "ArrowDown"
        ? { type: "shift", by: 1 }
        : null;
  }

  if (event.shiftKey) {
    return null;
  }

  const moves: Record<string, FlowMove> = {
    ArrowUp: "up",
    ArrowDown: "down",
    ArrowLeft: "left",
    ArrowRight: "right",
    Home: "home",
    End: "end",
    Escape: "parent",
  };
  const move = moves[event.key];

  if (move !== undefined) {
    return { type: "move", move };
  }

  switch (event.key) {
    case "Enter":
      return { type: "open" };
    case "Delete":
    case "Backspace":
      return { type: "remove" };
    default:
      return null;
  }
}

function listOf(index: TreeIndex, entry: TreeEntry): TreeEntry[] {
  return index.entries.filter(
    (other) => other.parent === entry.parent && other.body === entry.body,
  );
}

// The first node inside a container the flow goes into, or null where it goes past: a leaf, a collapsed container,
// or one with nothing inside.
function firstInside(node: SequenceStep, collapsed: ReadonlySet<string>): string | null {
  if (!isContainer(node) || collapsed.has(node.id)) {
    return null;
  }

  for (const body of bodiesOf(node)) {
    const [first] = body.steps;

    if (first !== undefined) {
      return first.id;
    }
  }

  return null;
}

// The last node the flow reaches going through a node: the node itself, or the last of its Then or its body.
function lastThrough(node: SequenceStep, collapsed: ReadonlySet<string>): string {
  if (!isContainer(node) || collapsed.has(node.id)) {
    return node.id;
  }

  for (const body of bodiesOf(node)) {
    const last = body.steps.at(-1);

    if (last !== undefined) {
      return lastThrough(last, collapsed);
    }
  }

  return node.id;
}

// What runs after a node once it and everything in it is done: its next sibling, or what runs after its container.
function after(index: TreeIndex, entry: TreeEntry): string | null {
  const next = listOf(index, entry)[entry.index + 1];

  if (next !== undefined) {
    return next.node.id;
  }

  const parent = entry.parent === null ? undefined : index.byId.get(entry.parent);

  return parent === undefined ? null : after(index, parent);
}

// The nearest node, the node itself or a container around it, that sits in a branch of an IF.
function inBranch(index: TreeIndex, entry: TreeEntry, branch: "then" | "else"): TreeEntry | null {
  let current: TreeEntry | undefined = entry;

  while (current !== undefined) {
    if (current.body === branch) {
      return current;
    }

    current = current.parent === null ? undefined : index.byId.get(current.parent);
  }

  return null;
}

// The node a move goes to from id, or null where there is none and the focus stays.
export function flowTarget(
  index: TreeIndex,
  id: string,
  move: FlowMove,
  collapsed: ReadonlySet<string> = new Set(),
): string | null {
  const entry = index.byId.get(id);
  const top = index.entries.filter((other) => other.parent === null);

  if (entry === undefined) {
    return top[0]?.node.id ?? null;
  }

  switch (move) {
    case "down":
      return firstInside(entry.node, collapsed) ?? after(index, entry);
    case "up": {
      const previous = listOf(index, entry)[entry.index - 1];

      return previous === undefined ? entry.parent : lastThrough(previous.node, collapsed);
    }
    case "left":
    case "right": {
      const from = inBranch(index, entry, move === "right" ? "then" : "else");
      const branch = index.byId.get(from?.parent ?? "");

      if (from === null || branch?.node.kind !== "if") {
        return null;
      }

      const other = move === "right" ? branch.node.else : branch.node.then;

      return (other[Math.min(from.index, other.length - 1)] ?? null)?.id ?? null;
    }
    case "home":
      return top[0]?.node.id ?? null;
    case "end":
      return top.at(-1)?.node.id ?? null;
    case "parent":
      return entry.parent;
  }
}

// Alt with Up or Down: the node trades places with its neighbour in its list; null at the end of it.
export function shiftEdit(index: TreeIndex, id: string, by: -1 | 1): FlowEdit | null {
  const entry = index.byId.get(id);

  if (entry === undefined) {
    return null;
  }

  const count = listOf(index, entry).length;
  const to = entry.index + by;

  if (to < 0 || to >= count) {
    return null;
  }

  return {
    type: "moveNodes",
    ids: [id],
    slot: { parent: entry.parent, body: entry.body, index: by > 0 ? entry.index + 2 : to },
  };
}

// The gap right after a node, where a paste or a copy goes; the end of the top list without one.
export function slotAfter(index: TreeIndex, id: string | null): Slot {
  const entry = id === null ? undefined : index.byId.get(id);

  if (entry === undefined) {
    return {
      parent: null,
      body: "steps",
      index: index.entries.filter((other) => other.parent === null).length,
    };
  }

  return { parent: entry.parent, body: entry.body, index: entry.index + 1 };
}

// The node that takes the focus once id is removed: the one after it, else the one before it, else its container.
export function afterRemoval(index: TreeIndex, id: string): string | null {
  const entry = index.byId.get(id);

  if (entry === undefined) {
    return null;
  }

  const list = listOf(index, entry);

  return list[entry.index + 1]?.node.id ?? list[entry.index - 1]?.node.id ?? entry.parent ?? null;
}
