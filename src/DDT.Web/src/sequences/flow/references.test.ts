// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { describe, expect, it } from "vitest";

import everyNodeJson from "@/test/fixtures/every-node.sequence.json";

import type { SequenceDraft } from "../sequenceDraft";
import type { SequenceDefinition } from "../sequences";
import { changedCondition, conditionPath, testsOf } from "./conditionTree";
import {
  referencesTo,
  renameInTemplate,
  renameReferences,
  templateNames,
  usedBy,
} from "./references";

const everyNode = everyNodeJson as unknown as SequenceDefinition;
const draft: SequenceDraft = {
  name: "Every node",
  description: "",
  steps: everyNode.steps,
  variables: everyNode.variables ?? [],
  inputs: everyNode.inputs ?? [],
};

const id = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, "0")}`;

describe("templates", () => {
  it("finds the names a template uses, each once, whatever their case and filters", () => {
    expect(
      templateNames(
        "PC-{{SerialNumber|alnum|right:12}} {{ office }} {{Office}} {{ bad name }} {{}}",
      ),
    ).toEqual(["SerialNumber", "office"]);
  });

  it("renames a name wherever it stands, keeping its spaces and filters, and nothing else", () => {
    expect(
      renameInTemplate("{{Office}}-{{ office|upper }}-{{Officer}}-{Office}", "Office", "Edition"),
    ).toBe("{{Edition}}-{{ Edition|upper }}-{{Officer}}-{Office}");
  });
});

describe("referencesTo", () => {
  it("names every place that uses a variable or an input, as a problem names its field", () => {
    expect(referencesTo(draft, "Office")).toEqual([
      { nodeId: id(12), field: "organizationalUnit" },
      { nodeId: id(9), field: "variable" },
      { nodeId: id(9), field: "value" },
    ]);
    expect(referencesTo(draft, "SerialNumber")).toEqual([
      { nodeId: null, field: "variables[1].default" },
    ]);
    expect(referencesTo(draft, "installer")).toEqual([{ nodeId: id(8), field: "runAs" }]);
    expect(referencesTo(draft, "JoinAccount")).toEqual([{ nodeId: id(12), field: "account" }]);
    expect(referencesTo(draft, "Phase")).toEqual([{ nodeId: id(5), field: "conditions[0]" }]);
    expect(referencesTo(draft, "DeviceKind")).toEqual([
      { nodeId: id(3), field: "when.parts[1].parts[0]" },
    ]);
    expect(referencesTo(draft, "LastExitCode")).toEqual([{ nodeId: id(6), field: "until" }]);
    expect(referencesTo(draft, "MemoryMegabytes")).toEqual([
      { nodeId: id(7), field: "test.parts[1]" },
      { nodeId: id(10), field: "when.parts[10]" },
      { nodeId: id(10), field: "when.parts[11]" },
      { nodeId: id(10), field: "when.parts[12]" },
      { nodeId: id(10), field: "when.parts[13]" },
    ]);
  });

  it("says which nodes use a name, each once", () => {
    expect(usedBy(draft, "Office")).toEqual([id(12), id(9)]);
    expect(usedBy(draft, "Model")).toEqual([id(10)]);
    expect(usedBy(draft, "Nothing")).toEqual([]);
  });

  it("renames every place, and leaves the declarations to the caller", () => {
    const renamed = renameReferences(draft, "Model", "Hardware");

    expect(referencesTo(renamed, "Model")).toEqual([]);
    expect(referencesTo(renamed, "Hardware")).toHaveLength(10);
    expect(renameReferences(draft, "JoinAccount", "Joiner").steps.at(-2)).toMatchObject({
      account: { accountId: null, input: "Joiner" },
    });
  });
});

describe("condition trees", () => {
  it("lists the tests with their paths and names the paths as problems do", () => {
    const tree = everyNode.steps[2]?.when ?? null;

    expect(testsOf(tree).map((placed) => conditionPath("when", placed.path))).toEqual([
      "when.parts[0]",
      "when.parts[1].parts[0]",
    ]);
    expect(conditionPath("test", [])).toBe("test");
  });

  it("replaces the root with set, even where there was none", () => {
    const test = {
      kind: "test" as const,
      variable: "Model",
      operator: "Equals" as const,
      value: "",
    };

    expect(changedCondition(null, [], { op: "set", node: test })).toBe(test);
    expect(changedCondition(null, [0], { op: "set", node: test })).toBeUndefined();
    expect(changedCondition(test, [], { op: "wrap", kind: "none" })).toEqual({
      kind: "none",
      parts: [test],
    });
  });
});
