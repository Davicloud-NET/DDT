// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";

import { equalJson } from "@/lib/equalJson";

import { walk } from "./flow/flowTree";
import {
  SEQUENCE_VERSION,
  type InputDeclaration,
  type SaveSequenceRequest,
  type SequenceDefinition,
  type SequenceStep,
  type SequenceView,
  type VariableDeclaration,
} from "./sequences";

// What the editor changes. An empty description is saved as null. Empty lists of variables and inputs are left out.
export interface SequenceDraft {
  name: string;
  description: string;
  steps: SequenceStep[];
  variables: VariableDeclaration[];
  inputs: InputDeclaration[];
}

export function draftOf(view: SequenceView): SequenceDraft {
  return {
    name: view.name,
    description: view.description ?? "",
    steps: view.definition.steps,
    variables: view.definition.variables ?? [],
    inputs: view.definition.inputs ?? [],
  };
}

export function definitionOf(draft: SequenceDraft): SequenceDefinition {
  return {
    version: SEQUENCE_VERSION,
    steps: draft.steps,
    ...(draft.variables.length > 0 ? { variables: draft.variables } : {}),
    ...(draft.inputs.length > 0 ? { inputs: draft.inputs } : {}),
  };
}

export function saveRequestOf(draft: SequenceDraft, revision: number): SaveSequenceRequest {
  return {
    revision,
    name: draft.name,
    description: draft.description.trim() === "" ? null : draft.description,
    definition: definitionOf(draft),
  };
}

export function sameDraft(a: SequenceDraft, b: SequenceDraft): boolean {
  return equalJson(a, b);
}

// A node without the nodes inside it, so a container counts as changed only for its own fields.
function ownFields(node: SequenceStep): object {
  switch (node.kind) {
    case "group":
    case "repeat":
      return { ...node, steps: [] };
    case "if":
      return { ...node, then: [], else: [] };
    default:
      return node;
  }
}

// What changed from one copy to another, named the way the editor shows it: "the name", "the description", the steps
// by name, "the order of the steps", "the variables" and "the inputs".
export function changedParts(from: SequenceDraft, to: SequenceDraft): string[] {
  const parts: string[] = [];

  if (from.name !== to.name) {
    parts.push(t`the name`);
  }

  if (from.description.trim() !== to.description.trim()) {
    parts.push(t`the description`);
  }

  const fromEntries = walk(from.steps);
  const toEntries = walk(to.steps);
  const before = new Map(fromEntries.map((entry) => [entry.node.id, entry.node]));
  const after = new Set(toEntries.map((entry) => entry.node.id));

  for (const { node } of toEntries) {
    const earlier = before.get(node.id);

    if (earlier === undefined || !equalJson(ownFields(earlier), ownFields(node))) {
      parts.push(node.name);
    }
  }

  for (const { node } of fromEntries) {
    if (!after.has(node.id)) {
      parts.push(node.name);
    }
  }

  // Where each node that both copies have sits: its container and body, in document order.
  const kept = (entries: typeof fromEntries, others: ReadonlySet<string>) =>
    entries
      .filter((entry) => others.has(entry.node.id))
      .map((entry) => [entry.node.id, entry.parent, entry.body]);

  if (!equalJson(kept(fromEntries, after), kept(toEntries, new Set(before.keys())))) {
    parts.push(t`the order of the steps`);
  }

  if (!equalJson(from.variables, to.variables)) {
    parts.push(t`the variables`);
  }

  if (!equalJson(from.inputs, to.inputs)) {
    parts.push(t`the inputs`);
  }

  return parts;
}
