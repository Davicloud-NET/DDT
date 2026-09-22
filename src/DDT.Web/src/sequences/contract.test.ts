// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { describe, expect, it } from "vitest";

import fixture from "@/test/fixtures/install-windows.sequence.json";

import { SEQUENCE_VERSION, type SequenceTemplate } from "./sequences";
import { isStepKind, newStep } from "./steps";

// The server writes the fixture from its Install Windows template (SequenceFixtureTests), so a field or kind
// renamed on one side only fails here. The mirror's own defaults, from newStep, stand for its types.
function sameShape(value: unknown, mirror: unknown): boolean {
  if (mirror === null) {
    return value === null || typeof value === "string";
  }

  if (Array.isArray(mirror)) {
    return Array.isArray(value);
  }

  return typeof value === typeof mirror;
}

describe("the sequence mirror", () => {
  const template: unknown = fixture;

  it("has the fields of the server's template and document", () => {
    expect(Object.keys(fixture).sort()).toEqual(
      ["key", "name", "description", "definition"].sort(),
    );
    expect(Object.keys(fixture.definition).sort()).toEqual(["steps", "version"]);
    expect(fixture.definition.version).toBe(SEQUENCE_VERSION);
  });

  it("knows every kind of step in the template", () => {
    const unknown = fixture.definition.steps
      .map((step) => step.kind)
      .filter((kind) => !isStepKind(kind));

    expect(unknown).toEqual([]);
  });

  it("has exactly the server's fields for each kind, with the same types", () => {
    for (const step of fixture.definition.steps) {
      if (!isStepKind(step.kind)) {
        throw new Error(`The mirror does not know ${step.kind}.`);
      }

      const mirror: Record<string, unknown> = { ...newStep(step.kind, step.id) };
      const sent: Record<string, unknown> = { ...step };

      expect({ kind: step.kind, fields: Object.keys(sent).sort() }).toEqual({
        kind: step.kind,
        fields: Object.keys(mirror).sort(),
      });

      for (const [field, value] of Object.entries(sent)) {
        expect({ kind: step.kind, field, matches: sameShape(value, mirror[field]) }).toEqual({
          kind: step.kind,
          field,
          matches: true,
        });
      }
    }
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
