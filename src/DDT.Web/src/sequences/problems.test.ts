// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { describe, expect, it } from "vitest";

import { branch, group, leaf } from "@/test/trees";

import {
  conditionPlace,
  declarationPlace,
  fieldFindings,
  isShownField,
  nodePhasesOf,
  parseFieldPath,
  sequenceFindings,
  withFieldAt,
} from "./problems";
import type { SequenceStep } from "./sequences";
import { newStep } from "./steps";

describe("the field a finding names", () => {
  it("is read into its parts", () => {
    expect(parseFieldPath("when.parts[1].value")).toEqual([
      { name: "when", index: null },
      { name: "parts", index: 1 },
      { name: "value", index: null },
    ]);
    expect(parseFieldPath("variables[2].name")).toEqual([
      { name: "variables", index: 2 },
      { name: "name", index: null },
    ]);
    expect(parseFieldPath("shares[0].path")).toEqual([
      { name: "shares", index: 0 },
      { name: "path", index: null },
    ]);
    expect(parseFieldPath(null)).toBeNull();
    expect(parseFieldPath("when..value")).toBeNull();
    expect(parseFieldPath("parts[x]")).toBeNull();
  });

  it("finds the place in a condition", () => {
    expect(conditionPlace("when.parts[1].value")).toEqual({
      condition: "when",
      path: [1],
      member: "value",
    });
    expect(conditionPlace("test.parts[0].parts[2].operator")).toEqual({
      condition: "test",
      path: [0, 2],
      member: "operator",
    });
    expect(conditionPlace("until")).toEqual({ condition: "until", path: [], member: null });
    expect(conditionPlace("conditions[1].value")).toEqual({
      condition: "conditions",
      path: [1],
      member: "value",
    });
    expect(conditionPlace("shares[0].path")).toBeNull();
    expect(conditionPlace("when.value.parts[0]")).toBeNull();
  });

  it("finds the place in the declarations", () => {
    expect(declarationPlace("variables[2].name")).toEqual({
      list: "variables",
      index: 2,
      member: "name",
    });
    expect(declarationPlace("inputs[0].choices[1].value")).toEqual({
      list: "inputs",
      index: 0,
      member: "choices",
    });
    expect(declarationPlace("variables")).toBeNull();
  });

  it("is shown at a field of the node where the inspector has one", () => {
    const script: SequenceStep = {
      ...newStep("runScript", "s"),
      conditions: [{ variable: "Model", operator: "Equals", value: "" }],
      shares: [{ path: "\\\\fs01\\deploy", account: { accountId: "a", input: null } }],
    };
    const test = branch("b", []);

    expect(isShownField(script, "conditions[0].value")).toBe(true);
    expect(isShownField(script, "conditions[1].value")).toBe(false);
    expect(isShownField(script, "when.parts[3].value")).toBe(true);
    expect(isShownField(script, "shares[0].path")).toBe(true);
    expect(isShownField(script, "shares[1].path")).toBe(false);
    expect(isShownField(script, "runAs")).toBe(true);
    expect(isShownField(script, "test")).toBe(false);
    expect(isShownField(test, "test.parts[0].value")).toBe(true);
    // An IF has only its test.
    expect(isShownField(test, "when")).toBe(false);
    expect(isShownField(script, null)).toBe(false);
  });

  it("keeps problems and warnings apart, and shows those of a whole condition at its value", () => {
    const findings = {
      problems: [{ stepId: "s", field: "when.parts[0]", message: "Compare with something." }],
      warnings: [{ stepId: "s", field: "when.parts[0].value", message: "Looks odd." }],
    };

    expect(fieldFindings(findings, "when.parts[0].value")).toEqual({
      problems: [],
      warnings: ["Looks odd."],
    });
    expect(
      fieldFindings(
        withFieldAt(findings, "when.parts[0]", "when.parts[0].value"),
        "when.parts[0].value",
      ),
    ).toEqual({ problems: ["Compare with something."], warnings: ["Looks odd."] });
  });

  it("belongs to the sequence where no node of the tree has it", () => {
    const steps = [group("g", leaf("a"))];
    const findings = {
      problems: [
        { stepId: "a", field: null, message: "Inside." },
        { stepId: "gone", field: null, message: "Gone." },
        { stepId: null, field: "variables[0].name", message: "Of the sequence." },
      ],
      warnings: [],
    };

    expect(sequenceFindings(findings, steps).problems.map((problem) => problem.message)).toEqual([
      "Gone.",
      "Of the sequence.",
    ]);
  });
});

describe("nodePhasesOf", () => {
  it("takes the server's phases of each node it has seen", () => {
    const steps = [leaf("a"), branch("b", [leaf("c")], [leaf("d")]), leaf("e")];

    expect(
      Object.fromEntries(
        nodePhasesOf(steps, {
          steps,
          stepPhases: [],
          nodePhases: [
            { nodeId: "a", phases: ["WindowsPE"] },
            { nodeId: "b", phases: ["WindowsPE"] },
            { nodeId: "c", phases: ["WindowsPE"] },
            { nodeId: "d", phases: ["Windows"] },
            { nodeId: "e", phases: ["WindowsPE", "Windows"] },
          ],
        }),
      ),
    ).toEqual({
      a: ["WindowsPE"],
      b: ["WindowsPE"],
      c: ["WindowsPE"],
      d: ["Windows"],
      e: ["WindowsPE", "Windows"],
    });
  });

  it("reads a server without node phases by its steps, and puts a new node in the phase of the one before it", () => {
    const saved = [leaf("a"), leaf("b")];
    const steps = [leaf("a"), leaf("b"), group("g", leaf("n"))];

    expect(
      Object.fromEntries(
        nodePhasesOf(steps, { steps: saved, stepPhases: ["WindowsPE", "Windows"] }),
      ),
    ).toEqual({ a: ["WindowsPE"], b: ["Windows"], g: ["Windows"], n: ["Windows"] });
  });
});
