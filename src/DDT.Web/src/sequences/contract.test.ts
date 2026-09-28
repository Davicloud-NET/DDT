// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { describe, expect, it } from "vitest";

import everyNodeJson from "@/test/fixtures/every-node.sequence.json";
import everyStep from "@/test/fixtures/every-step.sequence.json";
import fixture from "@/test/fixtures/install-windows.sequence.json";

import { requiredVersion, walk } from "./flow/flowTree";
import type { ConditionNode } from "./sequenceConditions";
import {
  SEQUENCE_VERSION,
  type AccountReference,
  type InputAsk,
  type InputDeclaration,
  type InputKind,
  type SequenceDefinition,
  type SequencePhase,
  type SequenceTemplate,
  type ShareConnection,
  type VariableDeclaration,
} from "./sequences";
import {
  allConditionOperators,
  conditionOperators,
  interpreters,
  isStepKind,
  newCondition,
  newStep,
  stepKinds,
} from "./steps";

// The server writes the fixtures (SequenceFixtureTests), so a field, kind or value renamed on one side only fails
// here. The version 3 one runs, and a run installs Windows or writes a raw disk image, so version 2's has the raw
// image's steps. The mirror's defaults, from newStep and newCondition, stand for its types; null stands for a text or
// a number that may be missing.
function sameShape(value: unknown, mirror: unknown): boolean {
  if (mirror === null) {
    return value === null || typeof value === "string" || typeof value === "number";
  }

  if (Array.isArray(mirror)) {
    return Array.isArray(value);
  }

  return typeof value === typeof mirror;
}

function expectMirrored(kind: string, sent: object, mirror: object) {
  const mirrored = mirror as Record<string, unknown>;

  expect({ kind, fields: Object.keys(sent).sort() }).toEqual({
    kind,
    fields: Object.keys(mirror).sort(),
  });

  for (const [field, value] of Object.entries(sent)) {
    expect({ kind, field, matches: sameShape(value, mirrored[field]) }).toEqual({
      kind,
      field,
      matches: true,
    });
  }
}

// Every phase, kind of input and place to ask, so one the server adds fails to compile here.
const phases = { WindowsPE: true, Windows: true } satisfies Record<SequencePhase, true>;
const inputKinds = {
  Text: true,
  Choice: true,
  MultiChoice: true,
  YesNo: true,
  Account: true,
} satisfies Record<InputKind, true>;
const inputAsks = { Both: true, Web: true, Machine: true } satisfies Record<InputAsk, true>;

// The members of version 3 the server leaves out while they are unset: on every kind, and on some.
const optionalOnEvery = ["when", "shares"];
const optionalOn: Partial<Record<string, readonly string[]>> = {
  runScript: ["runAs"],
  joinDomain: ["account"],
};

function optionalMembers(kind: string): string[] {
  return [...optionalOnEvery, ...(optionalOn[kind] ?? [])];
}

const everyNode = everyNodeJson as unknown as SequenceDefinition;
const nodes = walk(everyNode.steps).map((entry) => entry.node);
const steps: object[] = [...fixture.definition.steps, ...everyStep.steps, ...nodes];
const scripts = everyStep.steps.filter((step) => step.kind === "runScript");
const conditions = [
  ...everyStep.steps.flatMap((step) => step.conditions),
  ...nodes.flatMap((node) => node.conditions),
];

// A condition tree's nodes, the groups and the tests inside them included.
function conditionNodes(root: ConditionNode | null | undefined): ConditionNode[] {
  if (root === null || root === undefined) {
    return [];
  }

  return root.kind === "test" ? [root] : [root, ...root.parts.flatMap(conditionNodes)];
}

const trees = nodes.flatMap((node) => [
  ...conditionNodes(node.when),
  ...(node.kind === "if" ? conditionNodes(node.test) : []),
  ...(node.kind === "repeat" ? conditionNodes(node.until) : []),
]);

describe("the sequence mirror", () => {
  const template: unknown = fixture;

  it("has the fields of the server's template and documents", () => {
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
    expect(Object.keys(everyNode).sort()).toEqual(["inputs", "steps", "variables", "version"]);
    // The server stores a sequence with the lowest version it needs: 1 for Windows alone, 2 with a raw disk image,
    // 3 for a tree.
    expect(fixture.definition.version).toBe(1);
    expect(everyStep.version).toBe(2);
    expect(everyNode.version).toBe(SEQUENCE_VERSION);
  });

  it("works out the version a document needs as the server does", () => {
    expect(requiredVersion(fixture.definition as SequenceDefinition)).toBe(1);
    expect(requiredVersion(everyStep as SequenceDefinition)).toBe(2);
    expect(requiredVersion(everyNode)).toBe(3);
  });

  it("knows exactly the server's kinds of step", () => {
    expect(new Set([...nodes, ...everyStep.steps].map((node) => node.kind))).toEqual(
      new Set(stepKinds),
    );
  });

  it("has exactly the server's fields for each kind, with the same types", () => {
    for (const step of steps) {
      const { kind, id } = step as { kind: string; id: string };

      if (!isStepKind(kind)) {
        throw new Error(`The mirror does not know ${kind}.`);
      }

      const optional = optionalMembers(kind);
      const required = Object.fromEntries(
        Object.entries(step).filter(([field]) => !optional.includes(field)),
      );

      expectMirrored(kind, required, { ...newStep(kind, id) });
    }
  });

  it("reads the optional members of version 3 with the server's fields", () => {
    const account: AccountReference = { accountId: null, input: null };
    const share: ShareConnection = { path: "", account };
    const shares = nodes.flatMap((node) => node.shares ?? []);
    const references = [
      ...shares.map((connection) => connection.account),
      ...nodes.flatMap((node) =>
        node.kind === "runScript" && node.runAs
          ? [node.runAs]
          : node.kind === "joinDomain" && node.account
            ? [node.account]
            : [],
      ),
    ];

    expect(shares.length).toBeGreaterThan(0);
    expect(references).toHaveLength(3);
    expect(nodes.filter((node) => node.when).length).toBeGreaterThan(0);

    for (const connection of shares) {
      expectMirrored("share", connection, share);
    }

    for (const reference of references) {
      expectMirrored("account", reference, account);
    }
  });

  it("has exactly the server's fields for a condition, with the same types", () => {
    expect(conditions.length).toBeGreaterThan(0);

    for (const condition of conditions) {
      expectMirrored("condition", condition, { ...newCondition() });
    }
  });

  it("reads the server's condition trees", () => {
    expect(new Set(trees.map((node) => node.kind))).toEqual(
      new Set(["all", "any", "none", "test"]),
    );

    for (const node of trees) {
      expectMirrored(
        node.kind,
        node,
        node.kind === "test"
          ? { kind: "test", variable: "", operator: "", value: "" }
          : { kind: "all", parts: [] },
      );
    }
  });

  it("names the phases, interpreters and condition operators as the server does", () => {
    expect(new Set(scripts.map((step) => step.phase))).toEqual(new Set(Object.keys(phases)));
    expect(new Set(scripts.map((step) => step.interpreter))).toEqual(new Set(interpreters));
    expect(
      new Set(everyStep.steps.flatMap((step) => step.conditions).map((c) => c.operator)),
    ).toEqual(new Set(conditionOperators));
    expect(new Set(trees.flatMap((node) => (node.kind === "test" ? [node.operator] : [])))).toEqual(
      new Set(allConditionOperators),
    );
  });

  it("has exactly the server's fields for variables and inputs", () => {
    const variable: VariableDeclaration = {
      name: "",
      default: null,
      description: null,
      setBySteps: false,
    };
    const input: InputDeclaration = {
      name: "",
      label: "",
      help: null,
      kind: "Text",
      choices: [],
      default: null,
      required: false,
      maxLength: null,
      askAt: "Both",
      // An object, which null is to typeof as well.
      account: { domain: null, hosts: [], runAs: false },
    };
    const variables = everyNode.variables ?? [];
    const inputs = everyNode.inputs ?? [];

    expect(variables.length).toBeGreaterThan(0);

    for (const declared of variables) {
      expectMirrored("variable", declared, variable);
    }

    for (const asked of inputs) {
      expectMirrored("input", asked, input);
    }

    expect(new Set(inputs.map((asked) => asked.kind))).toEqual(new Set(Object.keys(inputKinds)));
    expect(new Set(inputs.map((asked) => asked.askAt))).toEqual(new Set(Object.keys(inputAsks)));

    for (const choice of inputs.flatMap((asked) => asked.choices)) {
      expectMirrored("choice", choice, { value: "", label: null });
    }

    for (const destination of inputs.flatMap((asked) => asked.account ?? [])) {
      expectMirrored("destination", destination, { domain: null, hosts: [], runAs: false });
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
