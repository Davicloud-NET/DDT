// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { ConditionOperator, StepCondition } from "../sequenceConditions";
import type { InputDeclaration, SequenceStep, VariableDeclaration } from "../sequences";
import { conditionOperators, isContainer, machineVariables } from "../steps";

// A sequence's steps as the tree they are, as the server's SequenceTree walks them: in pre-order, a node and then
// its bodies in order, Then before Else.

// The list a node sits in: its container's member, as the server's StepBody names it, and "steps" at the top.
export type BodyName = "steps" | "then" | "else";

export interface Body {
  name: BodyName;
  steps: SequenceStep[];
}

// A gap on a wire, where nodes can go: before the node at index of the list, or at its end when index is its length.
// An empty body has one slot, at 0. parent is null at the top.
export interface Slot {
  parent: string | null;
  body: BodyName;
  index: number;
}

export interface TreeEntry {
  node: SequenceStep;
  // The container the node sits in, null at the top, and the body of it.
  parent: string | null;
  body: BodyName;
  // Within its list, and within the walk, both from 0. depth is 0 at the top.
  index: number;
  order: number;
  depth: number;
  // Leaves are numbered from 1 in document order, as the flow and the rail show them; containers have none.
  number: number | null;
}

export interface TreeIndex {
  entries: TreeEntry[];
  // Where an id repeats, the first node with it, as the server's index keeps it; the validator reports the repeat.
  byId: ReadonlyMap<string, TreeEntry>;
}

export function bodiesOf(node: SequenceStep): Body[] {
  switch (node.kind) {
    case "group":
    case "repeat":
      return [{ name: "steps", steps: node.steps }];
    case "if":
      return [
        { name: "then", steps: node.then },
        { name: "else", steps: node.else },
      ];
    default:
      return [];
  }
}

export function bodyOf(node: SequenceStep, name: BodyName): SequenceStep[] | undefined {
  return bodiesOf(node).find((body) => body.name === name)?.steps;
}

export function walk(steps: readonly SequenceStep[]): TreeEntry[] {
  const entries: TreeEntry[] = [];
  let leaves = 0;

  const add = (
    list: readonly SequenceStep[],
    parent: string | null,
    body: BodyName,
    depth: number,
  ) => {
    list.forEach((node, index) => {
      entries.push({
        node,
        parent,
        body,
        index,
        order: entries.length,
        depth,
        number: isContainer(node) ? null : ++leaves,
      });

      for (const inside of bodiesOf(node)) {
        add(inside.steps, node.id, inside.name, depth + 1);
      }
    });
  };

  add(steps, null, "steps", 0);

  return entries;
}

export function indexTree(steps: readonly SequenceStep[]): TreeIndex {
  const entries = walk(steps);
  const byId = new Map<string, TreeEntry>();

  for (const entry of entries) {
    if (!byId.has(entry.node.id)) {
      byId.set(entry.node.id, entry);
    }
  }

  return { entries, byId };
}

export function findNode(steps: readonly SequenceStep[], id: string): SequenceStep | undefined {
  for (const node of steps) {
    if (node.id === id) {
      return node;
    }

    for (const body of bodiesOf(node)) {
      const found = findNode(body.steps, id);

      if (found !== undefined) {
        return found;
      }
    }
  }

  return undefined;
}

// The list a slot or a node's parent names: the top when parent is null, a container's body otherwise.
export function listAt(
  steps: SequenceStep[],
  parent: string | null,
  body: BodyName,
): SequenceStep[] | undefined {
  if (parent === null) {
    return body === "steps" ? steps : undefined;
  }

  const container = findNode(steps, parent);

  return container === undefined ? undefined : bodyOf(container, body);
}

// The containers around a node, its parent first.
export function ancestorsOf(index: TreeIndex, id: string): string[] {
  const ancestors: string[] = [];
  let parent = index.byId.get(id)?.parent ?? null;

  while (parent !== null && !ancestors.includes(parent)) {
    ancestors.push(parent);
    parent = index.byId.get(parent)?.parent ?? null;
  }

  return ancestors;
}

// Whether id is ancestorId or sits somewhere inside it.
export function isWithin(index: TreeIndex, id: string, ancestorId: string): boolean {
  return id === ancestorId || ancestorsOf(index, id).includes(ancestorId);
}

// The slot a node sits at: the gap just before it.
export function slotOf(index: TreeIndex, id: string): Slot | undefined {
  const entry = index.byId.get(id);

  return entry === undefined
    ? undefined
    : { parent: entry.parent, body: entry.body, index: entry.index };
}

// Every slot of the tree, in document order: the gap before each node, the slots inside it when it is a container,
// and the gap at the end of each list.
export function slotsOf(steps: readonly SequenceStep[]): Slot[] {
  const slots: Slot[] = [];

  const add = (list: readonly SequenceStep[], parent: string | null, body: BodyName) => {
    list.forEach((node, index) => {
      slots.push({ parent, body, index });

      for (const inside of bodiesOf(node)) {
        add(inside.steps, node.id, inside.name);
      }
    });
    slots.push({ parent, body, index: list.length });
  };

  add(steps, null, "steps");

  return slots;
}

export function sameSlot(a: Slot, b: Slot): boolean {
  return a.parent === b.parent && a.body === b.body && a.index === b.index;
}

// The server's SequenceTree.RequiredVersion: the lowest agent version that runs the definition as written. 2 for a raw
// image or cloud-init seed; 3 for tree parts (containers, Set variable, Pause, when, shares, runAs, a join's account,
// variables, inputs, operators after Contains) and for condition names an older agent would take as false.
export function requiredVersion(definition: {
  steps: readonly SequenceStep[];
  variables?: readonly VariableDeclaration[] | null;
  inputs?: readonly InputDeclaration[] | null;
}): number {
  let version =
    (definition.variables ?? []).length > 0 || (definition.inputs ?? []).length > 0 ? 3 : 1;

  for (const { node } of walk(definition.steps)) {
    version = Math.max(version, minimumVersion(node));

    if (
      (node.when ?? null) !== null ||
      (node.shares ?? []).length > 0 ||
      node.conditions.some(needsTree)
    ) {
      version = 3;
    }
  }

  return version;
}

function minimumVersion(node: SequenceStep): number {
  switch (node.kind) {
    case "writeRawImage":
    case "writeCloudInitSeed":
      return 2;
    case "group":
    case "if":
    case "repeat":
    case "setVariable":
    case "pause":
      return 3;
    case "runScript":
      return (node.runAs ?? null) === null ? 1 : 3;
    case "joinDomain":
      return (node.account ?? null) === null ? 1 : 3;
    default:
      return 1;
  }
}

const legacyOperators: ReadonlySet<ConditionOperator> = new Set(conditionOperators);
const legacyVariables: ReadonlySet<string> = new Set(machineVariables);

function needsTree(condition: StepCondition): boolean {
  return !legacyOperators.has(condition.operator) || !legacyVariables.has(condition.variable);
}
