// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { SequenceDraft } from "./sequenceDraft";
import type { SequenceStep, StepCondition, StepKind } from "./sequences";
import { newCondition, newStep } from "./steps";

// The fields of one kind of step, apart from what identifies it.
type FieldsOf<S> = S extends SequenceStep ? Partial<Omit<S, "id" | "kind">> : never;

export type StepPatch = FieldsOf<SequenceStep>;

// Every change the editor makes, as data, so a later editor can keep them for undo. New step ids are made by
// the functions below rather than by the reducer, which stays pure.
export type SequenceEdit =
  | { type: "rename"; name: string }
  | { type: "describe"; description: string }
  | { type: "addStep"; kind: StepKind; id: string }
  | { type: "insertStepAfter"; afterId: string; kind: StepKind; id: string }
  | { type: "removeStep"; id: string }
  | { type: "restoreStep"; step: SequenceStep; index: number }
  | { type: "moveStep"; id: string; to: number }
  | { type: "updateStep"; id: string; patch: StepPatch }
  | { type: "addCondition"; stepId: string }
  | { type: "updateCondition"; stepId: string; index: number; patch: Partial<StepCondition> }
  | { type: "removeCondition"; stepId: string; index: number };

export function addStep(kind: StepKind): SequenceEdit {
  return { type: "addStep", kind, id: crypto.randomUUID() };
}

export function insertStepAfter(afterId: string, kind: StepKind): SequenceEdit {
  return { type: "insertStepAfter", afterId, kind, id: crypto.randomUUID() };
}

// Typing waits for a pause before it is saved; a change of the structure is saved at once.
export function isTyping(edit: SequenceEdit): boolean {
  return (
    edit.type === "rename" ||
    edit.type === "describe" ||
    edit.type === "updateStep" ||
    edit.type === "updateCondition"
  );
}

// A patch names the fields of one kind. A field the step does not have is left out, so a step keeps the shape
// of its kind.
function patched(step: SequenceStep, patch: StepPatch): SequenceStep {
  const fields = Object.entries(patch).filter(
    ([key]) => key !== "id" && key !== "kind" && Object.hasOwn(step, key),
  );

  return { ...step, ...Object.fromEntries(fields) };
}

function withStep(
  draft: SequenceDraft,
  id: string,
  change: (step: SequenceStep) => SequenceStep,
): SequenceDraft {
  if (!draft.steps.some((step) => step.id === id)) {
    return draft;
  }

  return { ...draft, steps: draft.steps.map((step) => (step.id === id ? change(step) : step)) };
}

// A step whose id is there already is not added again, so a repeated restore changes nothing.
function inserted(draft: SequenceDraft, index: number, step: SequenceStep): SequenceDraft {
  const { steps } = draft;

  if (steps.some((existing) => existing.id === step.id)) {
    return draft;
  }

  const at = Math.max(0, Math.min(index, steps.length));

  return { ...draft, steps: [...steps.slice(0, at), step, ...steps.slice(at)] };
}

export function sequenceEdits(draft: SequenceDraft, edit: SequenceEdit): SequenceDraft {
  switch (edit.type) {
    case "rename":
      return { ...draft, name: edit.name };
    case "describe":
      return { ...draft, description: edit.description };
    case "addStep":
      return inserted(draft, draft.steps.length, newStep(edit.kind, edit.id));
    case "insertStepAfter": {
      const index = draft.steps.findIndex((step) => step.id === edit.afterId);

      return index < 0 ? draft : inserted(draft, index + 1, newStep(edit.kind, edit.id));
    }
    case "removeStep":
      return { ...draft, steps: draft.steps.filter((step) => step.id !== edit.id) };
    case "restoreStep":
      return inserted(draft, edit.index, edit.step);
    case "moveStep": {
      const from = draft.steps.findIndex((step) => step.id === edit.id);
      const to = Math.max(0, Math.min(edit.to, draft.steps.length - 1));
      const step = draft.steps[from];

      if (step === undefined || from === to) {
        return draft;
      }

      const rest = draft.steps.filter((other) => other.id !== edit.id);

      return { ...draft, steps: [...rest.slice(0, to), step, ...rest.slice(to)] };
    }
    case "updateStep":
      return withStep(draft, edit.id, (step) => patched(step, edit.patch));
    case "addCondition":
      return withStep(draft, edit.stepId, (step) => ({
        ...step,
        conditions: [...step.conditions, newCondition()],
      }));
    case "updateCondition":
      return withStep(draft, edit.stepId, (step) => ({
        ...step,
        conditions: step.conditions.map((condition, index) =>
          index === edit.index ? { ...condition, ...edit.patch } : condition,
        ),
      }));
    case "removeCondition":
      return withStep(draft, edit.stepId, (step) => ({
        ...step,
        conditions: step.conditions.filter((_, index) => index !== edit.index),
      }));
  }
}
