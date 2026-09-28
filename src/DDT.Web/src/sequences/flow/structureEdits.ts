// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { SequenceDraft } from "../sequenceDraft";
import type { ContainerStep, SequenceStep } from "../sequences";
import { isContainer } from "../steps";
import type { UnwrapKeep } from "./flowEdits";
import { indexTree, isWithin, walk, type BodyName, type Slot, type TreeIndex } from "./flowTree";
import { changeList, insertAt, withBody, without, withSteps } from "./treeChanges";

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

export function inserted(draft: SequenceDraft, slot: Slot, nodes: SequenceStep[]): SequenceDraft {
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

export function moved(draft: SequenceDraft, ids: string[], slot: Slot): SequenceDraft {
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

export function wrapped(
  draft: SequenceDraft,
  ids: string[],
  container: ContainerStep,
): SequenceDraft {
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

export function unwrapped(draft: SequenceDraft, id: string, keep: UnwrapKeep): SequenceDraft {
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
