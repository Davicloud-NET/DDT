// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { SequenceDraft } from "../sequenceDraft";
import type {
  ContainerKind,
  ContainerStep,
  InputDeclaration,
  SequenceStep,
  StepKind,
  VariableDeclaration,
} from "../sequences";
import { newStep } from "../steps";
import type { ConditionChange, ConditionField, ConditionPath } from "./conditionTree";
import { moveIn, named, renamed } from "./declarationEdits";
import { bodiesOf, type Slot } from "./flowTree";
import { conditionEdited, patchedNode } from "./nodeEdits";
import { sameName } from "./references";
import { inserted, moved, unwrapped, wrapped } from "./structureEdits";
import { insertAt, replaceNode, withBody, without, withSteps } from "./treeChanges";

// The flow builder's edits to the whole tree, as data that SequenceEdit extends. New ids come from insertNode,
// insertCopies and wrapIn, never from the reducer, so the reducer stays pure. An edit that doesn't fit, such as
// moving a node into itself, changes nothing.

type BodyField = "steps" | "then" | "else";
type NodeFieldsOf<S> = S extends SequenceStep ? Partial<Omit<S, "id" | "kind" | BodyField>> : never;

// The fields of one kind of node that an update may set. That leaves out what identifies it, and the bodies that
// hold other nodes, which only structural edits change. A version 3 member set to null is removed from the node.
export type NodePatch = NodeFieldsOf<SequenceStep>;

export type VariablePatch = Partial<Omit<VariableDeclaration, "name">>;

export type InputPatch = Partial<Omit<InputDeclaration, "name">>;

// Which body of an IF stays when the IF around it is removed.
export type UnwrapKeep = "then" | "else" | "both";

export type FlowEdit =
  | { type: "insertNodes"; slot: Slot; nodes: SequenceStep[] }
  // ids are siblings next to each other, in any order.
  | { type: "moveNodes"; ids: string[]; slot: Slot }
  | { type: "removeNodes"; ids: string[] }
  // The nodes go into the container's body, or into Then for an IF. The container goes where the first node was.
  | { type: "wrapNodes"; ids: string[]; container: ContainerStep }
  | { type: "unwrapNode"; id: string; keep?: UnwrapKeep }
  // chosen marks a patch made by a switch or a list, even though it sets a text field.
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
  // Renames a variable, the input of the same name, and every place that names them. It's saved at once, so a page
  // renames when the new name is complete, not at every key press.
  | { type: "renameVariable"; from: string; to: string }
  | { type: "addInput"; input: InputDeclaration; index?: number }
  | { type: "updateInput"; name: string; patch: InputPatch; chosen?: boolean }
  | { type: "removeInput"; name: string }
  | { type: "moveInput"; name: string; to: number };

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
