// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { flowEdits, type FlowEdit, type NodePatch } from "./flow/flowEdits";
import type { SequenceDraft } from "./sequenceDraft";
import type { StepCondition } from "./sequenceConditions";
import type { SequenceStep, StepKind } from "./sequences";
import { newCondition, newStep } from "./steps";

// The fields of one kind of step, apart from what identifies it and the nodes inside it.
export type StepPatch = NodePatch;

// Every change the editors make, as data, so undo can keep them. These edits work on the top-level steps, and
// FlowEdit works on the whole tree. New step ids come from addStep and insertStepAfter, so the reducer stays pure.
export type SequenceEdit =
  | { type: "rename"; name: string }
  | { type: "describe"; description: string }
  | { type: "addStep"; kind: StepKind; id: string }
  | { type: "insertStepAfter"; afterId: string; kind: StepKind; id: string }
  | { type: "removeStep"; id: string }
  | { type: "restoreStep"; step: SequenceStep; index: number }
  | { type: "moveStep"; id: string; to: number }
  // chosen marks a patch made by a switch, even though it sets a text field, such as turning a seed file on.
  | { type: "updateStep"; id: string; patch: StepPatch; chosen?: boolean }
  | { type: "addCondition"; stepId: string }
  | { type: "updateCondition"; stepId: string; index: number; patch: Partial<StepCondition> }
  | { type: "removeCondition"; stepId: string; index: number }
  | FlowEdit;

// The edits that add a step carry its id, so the page can show the new step.
export function addStep(kind: StepKind): Extract<SequenceEdit, { type: "addStep" }> {
  return { type: "addStep", kind, id: crypto.randomUUID() };
}

export function insertStepAfter(
  afterId: string,
  kind: StepKind,
): Extract<SequenceEdit, { type: "insertStepAfter" }> {
  return { type: "insertStepAfter", afterId, kind, id: crypto.randomUUID() };
}

// The fields set by a checkbox or a list instead of by typing, for a node, a condition's test, a variable and an
// input.
const chosenNodeFields: ReadonlySet<string> = new Set([
  "continueOnError",
  "rebootAfter",
  "requireMatch",
  "localAdministrator",
  "imageId",
  "phase",
  "interpreter",
  "packageId",
  "variable",
  "goOnAtLimit",
  "runAs",
  "account",
  "when",
  "test",
  "until",
]);
const chosenConditionFields: ReadonlySet<string> = new Set(["variable", "operator"]);
const chosenVariableFields: ReadonlySet<string> = new Set(["setBySteps"]);
const chosenInputFields: ReadonlySet<string> = new Set(["kind", "required", "askAt"]);

// The typed fields of a patch, joined. Null if it has none or a switch made it.
function typedFields(
  patch: object,
  chosen: ReadonlySet<string>,
  byChoice?: boolean,
): string | null {
  const typed = Object.keys(patch)
    .filter((field) => !chosen.has(field))
    .sort();

  return byChoice === true || typed.length === 0 ? null : typed.join(",");
}

function key(...parts: (string | null)[]): string | null {
  return parts.includes(null) ? null : parts.join(":");
}

// The field an edit types into, such as a node's script. Null for a structural change or a choice. Typing waits for
// a pause before it's saved, and undo takes back a stretch of typing in one field as one change.
export function typingKey(edit: SequenceEdit): string | null {
  switch (edit.type) {
    case "rename":
      return "name";
    case "describe":
      return "description";
    case "updateStep":
    case "updateNode":
      return key("node", edit.id, typedFields(edit.patch, chosenNodeFields, edit.chosen));
    case "updateCondition":
      return key(
        "conditions",
        edit.stepId,
        String(edit.index),
        typedFields(edit.patch, chosenConditionFields),
      );
    case "editCondition":
      return edit.change.op === "update"
        ? key(
            edit.field,
            edit.id,
            edit.path.join("."),
            typedFields(edit.change.patch, chosenConditionFields),
          )
        : null;
    case "updateVariable":
      return key("variable", edit.name, typedFields(edit.patch, chosenVariableFields, edit.chosen));
    case "updateInput":
      return key("input", edit.name, typedFields(edit.patch, chosenInputFields, edit.chosen));
    default:
      return null;
  }
}

export function isTyping(edit: SequenceEdit): boolean {
  return typingKey(edit) !== null;
}

// A patch names the fields of one kind. Fields the step doesn't have are left out, so a step keeps the shape of its
// kind.
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

// A step whose id is already there isn't added again, so a repeated restore changes nothing.
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
    default:
      return flowEdits(draft, edit);
  }
}
