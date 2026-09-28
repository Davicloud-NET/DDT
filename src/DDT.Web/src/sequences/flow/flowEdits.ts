// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { SequenceDraft } from "../sequenceDraft";
import type {
  ConditionNode,
  ContainerKind,
  ContainerStep,
  InputDeclaration,
  SequenceStep,
  StepKind,
  VariableDeclaration,
} from "../sequences";
import { isContainer, newStep } from "../steps";
import {
  changedCondition,
  conditionOf,
  type ConditionChange,
  type ConditionField,
  type ConditionPath,
} from "./conditionTree";
import {
  bodiesOf,
  bodyOf,
  findNode,
  indexTree,
  isWithin,
  walk,
  type BodyName,
  type Slot,
  type TreeIndex,
} from "./flowTree";
import { renameReferences, sameName } from "./references";

// The edits of the flow builder, as data like the step editor's, which they extend (SequenceEdit). They work on the
// whole tree. New ids come from the functions below, never from the reducer, which stays pure. An edit that does not
// fit the draft, such as a move into the nodes moved, leaves it as it is.

type BodyField = "steps" | "then" | "else";
type NodeFieldsOf<S> = S extends SequenceStep ? Partial<Omit<S, "id" | "kind" | BodyField>> : never;

// The fields of one kind of node an update may set: not what identifies it, and not the bodies that hold other nodes,
// which only the edits of the structure change. A member of version 3 set to null is taken out of the node.
export type NodePatch = NodeFieldsOf<SequenceStep>;

export type VariablePatch = Partial<Omit<VariableDeclaration, "name">>;

export type InputPatch = Partial<Omit<InputDeclaration, "name">>;

// Which body of an IF stays when the IF is taken away around it.
export type UnwrapKeep = "then" | "else" | "both";

export type FlowEdit =
  | { type: "insertNodes"; slot: Slot; nodes: SequenceStep[] }
  // ids are siblings next to each other, in any order.
  | { type: "moveNodes"; ids: string[]; slot: Slot }
  | { type: "removeNodes"; ids: string[] }
  // The nodes go into the container's body, or into Then for an IF, where the first of them was.
  | { type: "wrapNodes"; ids: string[]; container: ContainerStep }
  | { type: "unwrapNode"; id: string; keep?: UnwrapKeep }
  // chosen marks a patch made by a switch or a list although it sets a text field.
  | { type: "updateNode"; id: string; patch: NodePatch; chosen?: boolean }
  | {
      type: "editCondition";
      id: string;
      field: ConditionField;
      path: ConditionPath;
      change: ConditionChange;
    }
  | { type: "addVariable"; variable: VariableDeclaration; index?: number }
  | { type: "updateVariable"; name: string; patch: VariablePatch; chosen?: boolean }
  | { type: "removeVariable"; name: string }
  | { type: "moveVariable"; name: string; to: number }
  // Renames a variable, the input of the same name, and every place that names them. Saved at once, so a page
  // renames when the new name is complete rather than at every key.
  | { type: "renameVariable"; from: string; to: string }
  | { type: "addInput"; input: InputDeclaration; index?: number }
  | { type: "updateInput"; name: string; patch: InputPatch; chosen?: boolean }
  | { type: "removeInput"; name: string }
  | { type: "moveInput"; name: string; to: number };

// A name of a variable or an input, as the server allows it.
export const namePattern = /^[A-Za-z][A-Za-z0-9_]{0,63}$/;

// A new node of the kind at the slot. Its id is nodes[0].id.
export function insertNode(slot: Slot, kind: StepKind): Extract<FlowEdit, { type: "insertNodes" }> {
  return { type: "insertNodes", slot, nodes: [newStep(kind, crypto.randomUUID())] };
}

// Copies of nodes, such as those on the clipboard, with new ids for them and everything inside them.
export function withNewIds(nodes: readonly SequenceStep[]): SequenceStep[] {
  return nodes.map((node) => {
    let copy: SequenceStep = { ...node, id: crypto.randomUUID() };

    for (const body of bodiesOf(node)) {
      copy = withBody(copy, body.name, withNewIds(body.steps));
    }

    return copy;
  });
}

export function insertCopies(
  slot: Slot,
  nodes: readonly SequenceStep[],
): Extract<FlowEdit, { type: "insertNodes" }> {
  return { type: "insertNodes", slot, nodes: withNewIds(nodes) };
}

// Puts the nodes into a new container of the kind. Its id is container.id.
export function wrapIn(
  ids: string[],
  kind: ContainerKind,
): Extract<FlowEdit, { type: "wrapNodes" }> {
  return { type: "wrapNodes", ids, container: newStep(kind, crypto.randomUUID()) as ContainerStep };
}

function withBody(node: SequenceStep, name: BodyName, steps: SequenceStep[]): SequenceStep {
  switch (node.kind) {
    case "group":
    case "repeat":
      return name === "steps" ? { ...node, steps } : node;
    case "if":
      return name === "then"
        ? { ...node, then: steps }
        : name === "else"
          ? { ...node, else: steps }
          : node;
    default:
      return node;
  }
}

// The tree with the first node that has the id changed; the same lists where nothing changed.
function replaceNode(
  steps: SequenceStep[],
  id: string,
  change: (node: SequenceStep) => SequenceStep,
): SequenceStep[] {
  let done = false;
  // Read through a call, since visiting a body may set it.
  const finished = () => done;

  const visit = (list: SequenceStep[]): SequenceStep[] => {
    let changed: SequenceStep[] | null = null;

    for (const [index, node] of list.entries()) {
      if (done) {
        break;
      }

      let next = node;

      if (node.id === id) {
        next = change(node);
        done = true;
      } else {
        for (const body of bodiesOf(node)) {
          const inside = visit(body.steps);

          if (inside !== body.steps) {
            next = withBody(next, body.name, inside);
          }

          if (finished()) {
            break;
          }
        }
      }

      if (next !== node) {
        changed ??= [...list];
        changed[index] = next;
      }
    }

    return changed ?? list;
  };

  return visit(steps);
}

// The tree with the list a slot names changed, undefined where there is no such list.
function changeList(
  steps: SequenceStep[],
  parent: string | null,
  body: BodyName,
  change: (list: SequenceStep[]) => SequenceStep[],
): SequenceStep[] | undefined {
  if (parent === null) {
    return body === "steps" ? change(steps) : undefined;
  }

  const container = findNode(steps, parent);
  const list = container === undefined ? undefined : bodyOf(container, body);

  return list === undefined
    ? undefined
    : replaceNode(steps, parent, (node) => withBody(node, body, change(list)));
}

// The tree without the nodes with the ids, wherever they are.
function without(steps: SequenceStep[], ids: ReadonlySet<string>): SequenceStep[] {
  let changed = false;
  const kept: SequenceStep[] = [];

  for (const node of steps) {
    if (ids.has(node.id)) {
      changed = true;
      continue;
    }

    let next = node;

    for (const body of bodiesOf(node)) {
      const inside = without(body.steps, ids);

      if (inside !== body.steps) {
        next = withBody(next, body.name, inside);
      }
    }

    changed ||= next !== node;
    kept.push(next);
  }

  return changed ? kept : steps;
}

function withSteps(draft: SequenceDraft, steps: SequenceStep[] | undefined): SequenceDraft {
  return steps === undefined || steps === draft.steps ? draft : { ...draft, steps };
}

function insertAt<T>(list: readonly T[], index: number, items: readonly T[]): T[] {
  const at = Math.max(0, Math.min(index, list.length));

  return [...list.slice(0, at), ...items, ...list.slice(at)];
}

// Siblings next to each other: where they sit, in document order. Undefined for anything else.
interface Run {
  parent: string | null;
  body: BodyName;
  first: number;
  nodes: SequenceStep[];
}

function siblingRun(index: TreeIndex, ids: readonly string[]): Run | undefined {
  if (ids.length === 0 || new Set(ids).size !== ids.length) {
    return undefined;
  }

  const entries = ids.map((id) => index.byId.get(id));
  const [head] = entries;

  if (head === undefined || entries.some((entry) => entry === undefined)) {
    return undefined;
  }

  const sorted = entries.filter((entry) => entry !== undefined).sort((a, b) => a.index - b.index);
  const together = sorted.every(
    (entry, position) =>
      entry.parent === head.parent &&
      entry.body === head.body &&
      entry.index === (sorted[0]?.index ?? 0) + position,
  );

  return together
    ? {
        parent: head.parent,
        body: head.body,
        first: sorted[0]?.index ?? 0,
        nodes: sorted.map((entry) => entry.node),
      }
    : undefined;
}

function idsIn(nodes: readonly SequenceStep[]): string[] {
  return walk(nodes).map((entry) => entry.node.id);
}

function inserted(draft: SequenceDraft, slot: Slot, nodes: SequenceStep[]): SequenceDraft {
  const ids = idsIn(nodes);
  const existing = indexTree(draft.steps).byId;

  if (
    nodes.length === 0 ||
    new Set(ids).size !== ids.length ||
    ids.some((id) => existing.has(id))
  ) {
    return draft;
  }

  return withSteps(
    draft,
    changeList(draft.steps, slot.parent, slot.body, (list) => insertAt(list, slot.index, nodes)),
  );
}

function moved(draft: SequenceDraft, ids: string[], slot: Slot): SequenceDraft {
  const index = indexTree(draft.steps);
  const run = siblingRun(index, ids);

  if (
    run === undefined ||
    (slot.parent !== null && ids.some((id) => isWithin(index, slot.parent ?? "", id)))
  ) {
    return draft;
  }

  const count = run.nodes.length;
  const sameList = run.parent === slot.parent && run.body === slot.body;

  if (sameList && slot.index >= run.first && slot.index <= run.first + count) {
    return draft;
  }

  const target = sameList && slot.index > run.first ? slot.index - count : slot.index;
  const rest = without(draft.steps, new Set(ids));

  return withSteps(
    draft,
    changeList(rest, slot.parent, slot.body, (list) => insertAt(list, target, run.nodes)),
  );
}

function wrapped(draft: SequenceDraft, ids: string[], container: ContainerStep): SequenceDraft {
  const index = indexTree(draft.steps);
  const run = siblingRun(index, ids);

  if (run === undefined || idsIn([container]).some((id) => index.byId.has(id))) {
    return draft;
  }

  const wrapper = withBody(container, container.kind === "if" ? "then" : "steps", run.nodes);
  const moved = new Set(ids);

  return withSteps(
    draft,
    changeList(draft.steps, run.parent, run.body, (list) => [
      ...list.slice(0, run.first),
      wrapper,
      ...list.slice(run.first).filter((node) => !moved.has(node.id)),
    ]),
  );
}

function unwrapped(draft: SequenceDraft, id: string, keep: UnwrapKeep): SequenceDraft {
  const entry = indexTree(draft.steps).byId.get(id);
  const node = entry?.node;

  if (entry === undefined || node === undefined || !isContainer(node)) {
    return draft;
  }

  const inside =
    node.kind !== "if"
      ? node.steps
      : keep === "then"
        ? node.then
        : keep === "else"
          ? node.else
          : [...node.then, ...node.else];

  return withSteps(
    draft,
    changeList(draft.steps, entry.parent, entry.body, (list) => [
      ...list.slice(0, entry.index),
      ...inside,
      ...list.slice(entry.index + 1),
    ]),
  );
}

// The members of version 3 a node may be given although it has none yet.
function optionalMembers(node: SequenceStep): string[] {
  return [
    "when",
    "shares",
    ...(node.kind === "runScript" ? ["runAs"] : node.kind === "joinDomain" ? ["account"] : []),
  ];
}

const bodyFields: ReadonlySet<string> = new Set(["steps", "then", "else"]);

function patchedNode(node: SequenceStep, patch: NodePatch): SequenceStep {
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

function conditionEdited(
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

function moveIn<T>(list: readonly T[], from: number, to: number): T[] {
  const item = list[from];

  if (item === undefined) {
    return [...list];
  }

  const rest = list.filter((_, index) => index !== from);

  return insertAt(rest, Math.max(0, Math.min(to, rest.length)), [item]);
}

function named(list: readonly { name: string }[], name: string): number {
  return list.findIndex((item) => sameName(item.name, name));
}

function renamed(draft: SequenceDraft, from: string, to: string): SequenceDraft {
  const variable = named(draft.variables, from);
  const input = named(draft.inputs, from);
  const taken = (list: readonly { name: string }[], own: number) =>
    list.some((item, index) => index !== own && sameName(item.name, to));

  if (
    from === to ||
    !namePattern.test(to) ||
    (variable < 0 && input < 0) ||
    taken(draft.variables, variable) ||
    taken(draft.inputs, input)
  ) {
    return draft;
  }

  const references = renameReferences(draft, from, to);

  return {
    ...references,
    variables: references.variables.map((item, index) =>
      index === variable ? { ...item, name: to } : item,
    ),
    inputs: references.inputs.map((item, index) =>
      index === input ? { ...item, name: to } : item,
    ),
  };
}

export function flowEdits(draft: SequenceDraft, edit: FlowEdit): SequenceDraft {
  switch (edit.type) {
    case "insertNodes":
      return inserted(draft, edit.slot, edit.nodes);
    case "moveNodes":
      return moved(draft, edit.ids, edit.slot);
    case "removeNodes":
      return withSteps(draft, without(draft.steps, new Set(edit.ids)));
    case "wrapNodes":
      return wrapped(draft, edit.ids, edit.container);
    case "unwrapNode":
      return unwrapped(draft, edit.id, edit.keep ?? "both");
    case "updateNode":
      return withSteps(
        draft,
        replaceNode(draft.steps, edit.id, (node) => patchedNode(node, edit.patch)),
      );
    case "editCondition":
      return conditionEdited(draft, edit);
    case "addVariable":
      return named(draft.variables, edit.variable.name) >= 0
        ? draft
        : {
            ...draft,
            variables: insertAt(draft.variables, edit.index ?? draft.variables.length, [
              edit.variable,
            ]),
          };
    case "updateVariable": {
      const at = named(draft.variables, edit.name);

      return at < 0
        ? draft
        : {
            ...draft,
            variables: draft.variables.map((variable, index) =>
              index === at ? { ...variable, ...edit.patch, name: variable.name } : variable,
            ),
          };
    }
    case "removeVariable":
      return named(draft.variables, edit.name) < 0
        ? draft
        : {
            ...draft,
            variables: draft.variables.filter((variable) => !sameName(variable.name, edit.name)),
          };
    case "moveVariable": {
      const at = named(draft.variables, edit.name);

      return at < 0 || at === edit.to
        ? draft
        : { ...draft, variables: moveIn(draft.variables, at, edit.to) };
    }
    case "renameVariable":
      return renamed(draft, edit.from, edit.to);
    case "addInput":
      return named(draft.inputs, edit.input.name) >= 0
        ? draft
        : {
            ...draft,
            inputs: insertAt(draft.inputs, edit.index ?? draft.inputs.length, [edit.input]),
          };
    case "updateInput": {
      const at = named(draft.inputs, edit.name);

      return at < 0
        ? draft
        : {
            ...draft,
            inputs: draft.inputs.map((input, index) =>
              index === at ? { ...input, ...edit.patch, name: input.name } : input,
            ),
          };
    }
    case "removeInput":
      return named(draft.inputs, edit.name) < 0
        ? draft
        : { ...draft, inputs: draft.inputs.filter((input) => !sameName(input.name, edit.name)) };
    case "moveInput": {
      const at = named(draft.inputs, edit.name);

      return at < 0 || at === edit.to
        ? draft
        : { ...draft, inputs: moveIn(draft.inputs, at, edit.to) };
    }
  }
}
