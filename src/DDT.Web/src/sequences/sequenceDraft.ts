// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { equalJson } from "@/lib/equalJson";

import {
  SEQUENCE_VERSION,
  type SaveSequenceRequest,
  type SequenceStep,
  type SequenceView,
} from "./sequences";

// What the editor changes. An empty description is saved as none.
export interface SequenceDraft {
  name: string;
  description: string;
  steps: SequenceStep[];
}

export function draftOf(view: SequenceView): SequenceDraft {
  return {
    name: view.name,
    description: view.description ?? "",
    steps: view.definition.steps,
  };
}

export function saveRequestOf(draft: SequenceDraft, revision: number): SaveSequenceRequest {
  return {
    revision,
    name: draft.name,
    description: draft.description.trim() === "" ? null : draft.description,
    definition: { version: SEQUENCE_VERSION, steps: draft.steps },
  };
}

export function sameDraft(a: SequenceDraft, b: SequenceDraft): boolean {
  return equalJson(a, b);
}

// What one copy changed against another, named as the editor shows it: "the name", "the description",
// "the order of the steps" and the steps by name.
export function changedParts(from: SequenceDraft, to: SequenceDraft): string[] {
  const parts: string[] = [];

  if (from.name !== to.name) {
    parts.push("the name");
  }

  if (from.description.trim() !== to.description.trim()) {
    parts.push("the description");
  }

  const before = new Map(from.steps.map((step) => [step.id, step]));
  const after = new Set(to.steps.map((step) => step.id));

  for (const step of to.steps) {
    const earlier = before.get(step.id);

    if (earlier === undefined || !equalJson(earlier, step)) {
      parts.push(step.name);
    }
  }

  for (const step of from.steps) {
    if (!after.has(step.id)) {
      parts.push(step.name);
    }
  }

  const kept = (steps: SequenceStep[], others: Set<string>) =>
    steps.filter((step) => others.has(step.id)).map((step) => step.id);

  if (!equalJson(kept(from.steps, after), kept(to.steps, new Set(before.keys())))) {
    parts.push("the order of the steps");
  }

  return parts;
}
