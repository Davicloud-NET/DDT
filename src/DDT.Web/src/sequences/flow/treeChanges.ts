// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { SequenceDraft } from "../sequenceDraft";
import type { SequenceStep } from "../sequences";
import { bodiesOf, bodyOf, findNode, type BodyName } from "./flowTree";

export function withBody(node: SequenceStep, name: BodyName, steps: SequenceStep[]): SequenceStep {
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
export function replaceNode(
  steps: SequenceStep[],
  id: string,
  change: (node: SequenceStep) => SequenceStep,
): SequenceStep[] {
  let done = false;
  // Read through a call, since visiting a body may set it.
  const finished = () => done;

  // The node with its bodies visited, up to the one that held the node with the id.
  const visitBodies = (node: SequenceStep): SequenceStep => {
    let next = node;

    for (const body of bodiesOf(node)) {
      const inside = visit(body.steps);

      if (inside !== body.steps) {
        next = withBody(next, body.name, inside);
      }

      if (finished()) {
        break;
      }
    }

    return next;
  };

  const visit = (list: SequenceStep[]): SequenceStep[] => {
    let changed: SequenceStep[] | null = null;

    for (const [index, node] of list.entries()) {
      if (done) {
        break;
      }

      let next: SequenceStep;

      if (node.id === id) {
        next = change(node);
        done = true;
      } else {
        next = visitBodies(node);
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
export function changeList(
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
export function without(steps: SequenceStep[], ids: ReadonlySet<string>): SequenceStep[] {
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

export function withSteps(draft: SequenceDraft, steps: SequenceStep[] | undefined): SequenceDraft {
  return steps === undefined || steps === draft.steps ? draft : { ...draft, steps };
}

export function insertAt<T>(list: readonly T[], index: number, items: readonly T[]): T[] {
  const at = Math.max(0, Math.min(index, list.length));

  return [...list.slice(0, at), ...items, ...list.slice(at)];
}
