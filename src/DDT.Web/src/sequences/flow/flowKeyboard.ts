// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";

import type { SequenceStep } from "../sequences";
import { findingCounts } from "../sequenceList";
import { isContainer, isStepKind, stepKindLabel } from "../steps";
import type { FlowEdit } from "./flowEdits";
import { bodiesOf, type Slot, type TreeEntry, type TreeIndex } from "./flowTree";

// The keys of the flow canvas, worked out from the tree alone. The canvas is one stop of the Tab key; within it the
// arrow keys go from node to node the way the flow runs: Down goes on to what runs next, into the Then of an IF and
// into a group or a repeat, and on past the join after the last node of a branch; Up goes back the same way. Left
// and Right go across to the other branch of the nearest IF, Home and End to the first and the last node, Escape
// out to the container around.

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

// A node's name as the flow shows it: containers say their kind first.
export function nodeTitle(node: SequenceStep): string {
  const name = node.name.trim() === "" ? t`Unnamed step` : node.name;

  switch (node.kind) {
    case "if":
      return t`If: ${name}`;
    case "group":
      return t`Group: ${name}`;
    case "repeat":
      return t`Repeat: ${name}`;
    default:
      return name;
  }
}

// Where a node sits, such as "Step 2 of Then of 'If: Is it a Latitude?'" or, at the top, "Step 3".
export function placeLabel(index: TreeIndex, entry: TreeEntry): string {
  const position = entry.index + 1;
  const container = entry.parent === null ? undefined : index.byId.get(entry.parent)?.node;

  if (container === undefined) {
    return t`Step ${position}`;
  }

  const title = nodeTitle(container);

  switch (entry.body) {
    case "then":
      return t`Step ${position} of Then of ''${title}''`;
    case "else":
      return t`Step ${position} of Else of ''${title}''`;
    default:
      return t`Step ${position} of ''${title}''`;
  }
}

// A node as a screen reader says it: where it is, its name, its kind where the name is not that, and its findings,
// such as "Step 2 of Then of 'If: Is it a Latitude?', Apply image, 1 problem".
export function nodeLabel(
  index: TreeIndex,
  id: string,
  problems: number,
  warnings: number,
): string {
  const entry = index.byId.get(id);

  if (entry === undefined) {
    return "";
  }

  const title = nodeTitle(entry.node);
  const kind = stepKindLabel(entry.node.kind);

  return [
    placeLabel(index, entry),
    title,
    isContainer(entry.node) || title === kind ? null : kind,
    findingCounts(problems, warnings),
  ]
    .filter((part) => part !== null)
    .join(", ");
}

// A gap as the key that adds there says it.
export function slotLabel(index: TreeIndex, slot: Slot): string {
  const list = index.entries.filter(
    (entry) => entry.parent === slot.parent && entry.body === slot.body,
  );
  const next = list[slot.index]?.node;
  const previous = list[slot.index - 1]?.node;

  if (previous !== undefined && next !== undefined) {
    const before = nodeTitle(previous);
    const following = nodeTitle(next);

    return t`Add a step between ${before} and ${following}`;
  }

  if (next !== undefined) {
    const name = nodeTitle(next);

    return t`Add a step before ${name}`;
  }

  if (previous !== undefined) {
    const name = nodeTitle(previous);

    return t`Add a step after ${name}`;
  }

  const container = slot.parent === null ? undefined : index.byId.get(slot.parent)?.node;

  if (container === undefined) {
    return t`Add the first step`;
  }

  const title = nodeTitle(container);

  switch (slot.body) {
    case "then":
      return t`Add a step to Then of ''${title}''`;
    case "else":
      return t`Add a step to Else of ''${title}''`;
    default:
      return t`Add a step to ''${title}''`;
  }
}

// What Ctrl+C puts on the clipboard: the nodes as the document holds them, marked as DDT's, so a paste can tell them
// from other text.
export interface FlowClipboard {
  ddtFlow: 1;
  nodes: SequenceStep[];
}

export function clipboardText(nodes: readonly SequenceStep[]): string {
  return JSON.stringify({ ddtFlow: 1, nodes: [...nodes] } satisfies FlowClipboard);
}

function isNode(value: unknown): value is SequenceStep {
  if (typeof value !== "object" || value === null) {
    return false;
  }

  const node = value as Record<string, unknown>;

  if (
    typeof node.kind !== "string" ||
    !isStepKind(node.kind) ||
    typeof node.id !== "string" ||
    typeof node.name !== "string" ||
    !Array.isArray(node.conditions) ||
    typeof node.continueOnError !== "boolean" ||
    typeof node.rebootAfter !== "boolean"
  ) {
    return false;
  }

  const bodies =
    node.kind === "if"
      ? [node.then, node.else]
      : node.kind === "group" || node.kind === "repeat"
        ? [node.steps]
        : [];

  return bodies.every((body) => Array.isArray(body) && body.every(isNode));
}

// The nodes of text Ctrl+C put on the clipboard, or null for any other text. Their ids are the ones copied; a paste
// gives them new ones.
export function nodesFromClipboard(text: string): SequenceStep[] | null {
  try {
    const value: unknown = JSON.parse(text);

    if (typeof value !== "object" || value === null) {
      return null;
    }

    const { ddtFlow, nodes } = value as Partial<Record<keyof FlowClipboard, unknown>>;

    return ddtFlow === 1 && Array.isArray(nodes) && nodes.length > 0 && nodes.every(isNode)
      ? nodes
      : null;
  } catch {
    return null;
  }
}
