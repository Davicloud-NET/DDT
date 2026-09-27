// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { describe, expect, it } from "vitest";

import everyStep from "@/test/fixtures/every-step.sequence.json";
import fixture from "@/test/fixtures/install-windows.sequence.json";

import { SEQUENCE_VERSION, type SequencePhase, type SequenceTemplate } from "./sequences";
import {
  conditionOperators,
  interpreters,
  isStepKind,
  newCondition,
  newStep,
  stepKinds,
} from "./steps";

// The server writes the fixtures (SequenceFixtureTests): its Install Windows template, and a document with every
// kind of step, phase, interpreter and condition operator. So a field, kind or value renamed on one side only
// fails here. The mirror's own defaults, from newStep and newCondition, stand for its types.
function sameShape(value: unknown, mirror: unknown): boolean {
  if (mirror === null) {
    return value === null || typeof value === "string";
  }

  if (Array.isArray(mirror)) {
    return Array.isArray(value);
  }

  return typeof value === typeof mirror;
}

function expectMirrored(
  kind: string,
  sent: Record<string, unknown>,
  mirror: Record<string, unknown>,
) {
  expect({ kind, fields: Object.keys(sent).sort() }).toEqual({
    kind,
    fields: Object.keys(mirror).sort(),
  });

  for (const [field, value] of Object.entries(sent)) {
    expect({ kind, field, matches: sameShape(value, mirror[field]) }).toEqual({
      kind,
      field,
      matches: true,
    });
  }
}

// Every phase, so a phase the server adds fails to compile here.
const phases = { WindowsPE: true, Windows: true } satisfies Record<SequencePhase, true>;

const steps: Record<string, unknown>[] = [...fixture.definition.steps, ...everyStep.steps];
const scripts = everyStep.steps.filter((step) => step.kind === "runScript");
const conditions = everyStep.steps.flatMap((step) => step.conditions);

describe("the sequence mirror", () => {
  const template: unknown = fixture;

  it("has the fields of the server's template and document", () => {
    expect(Object.keys(fixture).sort()).toEqual(
      [
        "key",
        "name",
        "description",
        "definition",
        "nameCode",
        "nameArgs",
        "descriptionCode",
        "descriptionArgs",
      ].sort(),
    );
    expect(Object.keys(fixture.definition).sort()).toEqual(["steps", "version"]);
    expect(Object.keys(everyStep).sort()).toEqual(["steps", "version"]);
    // The server stores a sequence with the lowest version its kinds need: 1 for Windows alone.
    expect(fixture.definition.version).toBe(1);
    expect(everyStep.version).toBe(SEQUENCE_VERSION);
  });

  it("knows exactly the server's kinds of step", () => {
    expect(new Set(everyStep.steps.map((step) => step.kind))).toEqual(new Set(stepKinds));
  });

  it("has exactly the server's fields for each kind, with the same types", () => {
    for (const step of steps) {
      const kind = String(step.kind);
      const id = String(step.id);

      if (!isStepKind(kind)) {
        throw new Error(`The mirror does not know ${kind}.`);
      }

      expectMirrored(kind, step, { ...newStep(kind, id) });
    }
  });

  it("has exactly the server's fields for a condition, with the same types", () => {
    expect(conditions.length).toBeGreaterThan(0);

    for (const condition of conditions) {
      expectMirrored("condition", condition, { ...newCondition() });
    }
  });

  it("names the phases, interpreters and condition operators as the server does", () => {
    expect(new Set(scripts.map((step) => step.phase))).toEqual(new Set(Object.keys(phases)));
    expect(new Set(scripts.map((step) => step.interpreter))).toEqual(new Set(interpreters));
    expect(new Set(conditions.map((condition) => condition.operator))).toEqual(
      new Set(conditionOperators),
    );
  });

  it("reads the template as the page uses it", () => {
    const read = template as SequenceTemplate;

    expect(read.definition.steps.map((step) => step.kind)).toEqual([
      "partition",
      "applyImage",
      "injectDrivers",
      "writeUnattend",
      "joinDomain",
    ]);
  });
});
