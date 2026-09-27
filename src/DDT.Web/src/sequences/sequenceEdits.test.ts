// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { describe, expect, it } from "vitest";

import { changedParts, type SequenceDraft } from "./sequenceDraft";
import {
  addStep,
  insertStepAfter,
  isTyping,
  sequenceEdits,
  type SequenceEdit,
} from "./sequenceEdits";
import { newStep } from "./steps";

function draft(...ids: string[]): SequenceDraft {
  return {
    name: "Lab",
    description: "",
    steps: ids.map((id) => ({ ...newStep("runScript", id), name: `Script ${id}` })),
  };
}

function apply(start: SequenceDraft, ...edits: SequenceEdit[]): SequenceDraft {
  return edits.reduce(sequenceEdits, start);
}

const order = (result: SequenceDraft) => result.steps.map((step) => step.id);

describe("sequenceEdits", () => {
  it("renames and describes the sequence", () => {
    const result = apply(
      draft("a"),
      { type: "rename", name: "Lab PCs" },
      { type: "describe", description: "For room 4" },
    );

    expect(result.name).toBe("Lab PCs");
    expect(result.description).toBe("For room 4");
  });

  it("adds a step with the kind's defaults at the end, and inserts one after a step", () => {
    const result = apply(
      draft("a", "b"),
      { type: "addStep", kind: "partition", id: "p" },
      { type: "insertStepAfter", afterId: "a", kind: "reboot", id: "r" },
    );

    expect(order(result)).toEqual(["a", "r", "b", "p"]);
    expect(result.steps[3]).toEqual({
      kind: "partition",
      id: "p",
      name: "Partition the disk",
      conditions: [],
      continueOnError: false,
      rebootAfter: false,
      systemPartitionMegabytes: 300,
      recoveryPartitionMegabytes: 1024,
    });
  });

  it("gives every new step an id of its own, and never adds one id twice", () => {
    const added = addStep("reboot");
    const result = apply(draft("a"), added, insertStepAfter("a", "reboot"), added);

    expect(new Set(order(result)).size).toBe(3);
    expect(result.steps).toHaveLength(3);
  });

  it("moves a step within the bounds of the list", () => {
    const start = draft("a", "b", "c");

    expect(order(apply(start, { type: "moveStep", id: "c", to: 0 }))).toEqual(["c", "a", "b"]);
    expect(order(apply(start, { type: "moveStep", id: "a", to: 1 }))).toEqual(["b", "a", "c"]);
    expect(order(apply(start, { type: "moveStep", id: "a", to: 9 }))).toEqual(["b", "c", "a"]);
    expect(order(apply(start, { type: "moveStep", id: "b", to: -1 }))).toEqual(["b", "a", "c"]);
    expect(apply(start, { type: "moveStep", id: "x", to: 0 })).toBe(start);
  });

  it("brings a removed step back where it was", () => {
    const start = draft("a", "b", "c");
    const removed = start.steps[1];

    if (removed === undefined) {
      throw new Error("The draft has no second step.");
    }

    const without = apply(start, { type: "removeStep", id: "b" });
    expect(order(without)).toEqual(["a", "c"]);

    const restored = apply(without, { type: "restoreStep", step: removed, index: 1 });
    expect(restored).toEqual(start);

    // Restoring twice keeps one copy.
    expect(apply(restored, { type: "restoreStep", step: removed, index: 0 })).toBe(restored);
  });

  it("changes only fields the step's kind has", () => {
    const result = apply(
      draft("a"),
      { type: "updateStep", id: "a", patch: { script: "exit 0", timeoutMinutes: 5 } },
      { type: "updateStep", id: "a", patch: { imageId: "not a script field" } },
    );

    expect(result.steps[0]).toMatchObject({
      kind: "runScript",
      script: "exit 0",
      timeoutMinutes: 5,
    });
    expect(result.steps[0]).not.toHaveProperty("imageId");
  });

  it("adds, changes and removes conditions", () => {
    const result = apply(
      draft("a"),
      { type: "addCondition", stepId: "a" },
      { type: "addCondition", stepId: "a" },
      { type: "updateCondition", stepId: "a", index: 1, patch: { value: "Latitude 7440" } },
      { type: "removeCondition", stepId: "a", index: 0 },
    );

    expect(result.steps[0]?.conditions).toEqual([
      { variable: "Model", operator: "Equals", value: "Latitude 7440" },
    ]);
  });

  it("names what one copy changed against another", () => {
    const base = draft("a", "b", "c");
    const theirs = apply(
      base,
      { type: "rename", name: "Lab PCs" },
      { type: "updateStep", id: "a", patch: { script: "exit 3" } },
      { type: "removeStep", id: "c" },
      { type: "moveStep", id: "b", to: 0 },
    );

    expect(changedParts(base, theirs)).toEqual([
      "the name",
      "Script a",
      "Script c",
      "the order of the steps",
    ]);
    expect(changedParts(base, base)).toEqual([]);
  });
});

describe("isTyping", () => {
  it("waits for a pause only after typing, and saves choices and structure at once", () => {
    const typed: SequenceEdit[] = [
      { type: "rename", name: "Lab" },
      { type: "describe", description: "Room 4" },
      { type: "updateStep", id: "a", patch: { name: "Set wallpaper" } },
      { type: "updateStep", id: "a", patch: { script: "exit 0" } },
      { type: "updateStep", id: "a", patch: { timeoutMinutes: 90 } },
      { type: "updateCondition", stepId: "a", index: 0, patch: { value: "Latitude" } },
    ];
    const chosen: SequenceEdit[] = [
      { type: "updateStep", id: "a", patch: { continueOnError: true } },
      { type: "updateStep", id: "a", patch: { rebootAfter: true } },
      { type: "updateStep", id: "a", patch: { requireMatch: true } },
      { type: "updateStep", id: "a", patch: { localAdministrator: true } },
      { type: "updateStep", id: "a", patch: { imageId: "0193a4b2-0000-7000-8000-0000000000a1" } },
      { type: "updateStep", id: "a", patch: { phase: "Windows" } },
      { type: "updateStep", id: "a", patch: { interpreter: "PowerShell" } },
      { type: "updateStep", id: "a", patch: { packageId: null } },
      { type: "updateStep", id: "a", patch: { networkConfig: "version: 2\n" }, chosen: true },
      { type: "updateCondition", stepId: "a", index: 0, patch: { operator: "Contains" } },
      { type: "updateCondition", stepId: "a", index: 0, patch: { variable: "Manufacturer" } },
      addStep("reboot"),
      { type: "moveStep", id: "a", to: 1 },
    ];

    expect(typed.filter((edit) => !isTyping(edit))).toEqual([]);
    expect(chosen.filter(isTyping)).toEqual([]);
  });
});
