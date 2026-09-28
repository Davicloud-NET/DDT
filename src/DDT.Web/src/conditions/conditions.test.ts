// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { describe, expect, it } from "vitest";

import type { ConditionNode, InputDeclaration } from "@/sequences/sequences";

import {
  conditionSentence,
  conditionSummary,
  conditionText,
  factCatalogue,
  gigabytesOf,
  legacyPath,
  legacyTree,
  megabytesOf,
  newTestOf,
  operatorsFor,
  subjectFor,
  subjectsOf,
  valueProblem,
  withSubject,
} from "./conditions";

const office: InputDeclaration = {
  name: "Office",
  label: "Office",
  help: null,
  kind: "Choice",
  choices: [
    { value: "Standard", label: null },
    { value: "ProPlus", label: "Professional Plus" },
  ],
  default: null,
  required: false,
  maxLength: null,
  askAt: "Web",
  account: null,
};

const joinAccount: InputDeclaration = {
  ...office,
  name: "JoinAccount",
  kind: "Account",
  choices: [],
  askAt: "Machine",
};

const subjects = subjectsOf({
  facts: factCatalogue,
  valueNames: ["TimeZone", "Office", "model"],
  variables: [
    { name: "ComputerName2", default: null, description: null, setBySteps: true },
    { name: "Office", default: "Standard", description: null, setBySteps: false },
  ],
  inputs: [office, joinAccount],
});

describe("the subjects of a condition", () => {
  it("lists the facts, the run's values, the values of rules and roles, and the sequence's own, each once", () => {
    const sections = subjects.map((subject) => `${subject.section}:${subject.name}`);

    expect(sections.slice(0, 3)).toEqual([
      "machine:Manufacturer",
      "machine:Model",
      "machine:FriendlyModel",
    ]);
    expect(sections).toContain("run:LastStepFailed");
    expect(sections).toContain("values:TimeZone");
    // A value a rule sets under the name of a fact or of the sequence's own is that one.
    expect(sections.filter((entry) => entry.toLowerCase().endsWith(":model"))).toEqual([
      "machine:Model",
    ]);
    expect(sections.slice(-2)).toEqual(["sequence:ComputerName2", "sequence:Office"]);
    // An Account input sets no value.
    expect(sections).not.toContain("sequence:JoinAccount");
  });

  it("types each subject, with its choices and where an input is asked", () => {
    expect(subjectFor(subjects, "memorymegabytes")).toMatchObject({
      label: "Memory",
      kind: "memory",
    });
    expect(subjectFor(subjects, "DeviceKind").choices.map((choice) => choice.value)).toEqual([
      "Laptop",
      "Desktop",
      "Tablet",
      "Server",
      "Virtual",
      "Unknown",
    ]);
    expect(subjectFor(subjects, "Office")).toMatchObject({
      kind: "oneOf",
      note: "asked on the web",
      choices: [
        { value: "Standard", label: "Standard" },
        { value: "ProPlus", label: "Professional Plus" },
      ],
    });
    expect(subjectFor(subjects, "Nothing")).toMatchObject({ kind: "text", label: "Nothing" });
  });

  it("offers the operators that fit the kind", () => {
    expect(operatorsFor("yesNo")).toEqual(["Equals", "NotEquals", "Exists", "NotExists"]);
    expect(operatorsFor("ipv4")[0]).toBe("InSubnet");
    expect(operatorsFor("memory")).not.toContain("Contains");
    expect(operatorsFor("text")).toContain("Matches");
  });

  it("keeps the operator and the value where they fit a new subject", () => {
    const model = subjectFor(subjects, "Model");
    const friendly = subjectFor(subjects, "FriendlyModel");
    const tpm = subjectFor(subjects, "TpmPresent");
    const test = {
      kind: "test" as const,
      variable: "Model",
      operator: "Contains" as const,
      value: "T14",
    };

    expect(withSubject(test, model, friendly)).toEqual({ ...test, variable: "FriendlyModel" });
    expect(withSubject(test, model, tpm)).toEqual({
      kind: "test",
      variable: "TpmPresent",
      operator: "Equals",
      value: "true",
    });
    expect(newTestOf(subjectFor(subjects, "DeviceKind"))).toMatchObject({ value: "Laptop" });
  });
});

describe("values", () => {
  it("enters memory in GB and stores it in MB", () => {
    expect(megabytesOf("8")).toBe("8192");
    expect(megabytesOf("1,5")).toBe("1536");
    expect(gigabytesOf("16384")).toBe("16");
    expect(gigabytesOf("8000")).toBe("7.81");
    expect(megabytesOf("lots")).toBe("lots");
  });

  it("says what is wrong with a value before the server does", () => {
    expect(valueProblem("ipv4", "InSubnet", "10.20.0.0/16")).toBeNull();
    expect(valueProblem("ipv4", "InSubnet", "10.20.0.0")).toBe(
      "Enter a network such as 10.20.0.0/16.",
    );
    expect(valueProblem("ipv4", "Equals", "10.20.300.1")).toBe(
      "Enter an IPv4 address such as 10.0.4.51.",
    );
    expect(valueProblem("mac", "Equals", "00-15-5D-01-02-03")).toBeNull();
    expect(valueProblem("mac", "StartsWith", "00:15:5D")).toBeNull();
    expect(valueProblem("mac", "Equals", "00:15:5D")).toBe(
      "Enter a MAC address such as 00:15:5D:01:02:03.",
    );
    expect(valueProblem("number", "In", "4; 8;x")).toBe("Enter a number.");
    expect(valueProblem("number", "Exists", "x")).toBeNull();
    expect(valueProblem("text", "Equals", "anything")).toBeNull();
  });
});

describe("a condition in words", () => {
  const tree: ConditionNode = {
    kind: "all",
    parts: [
      { kind: "test", variable: "Model", operator: "Contains", value: "Latitude" },
      { kind: "test", variable: "MemoryMegabytes", operator: "GreaterOrEqual", value: "8192" },
      {
        kind: "none",
        parts: [
          { kind: "test", variable: "DeviceKind", operator: "Equals", value: "Virtual" },
          { kind: "test", variable: "TpmPresent", operator: "Equals", value: "false" },
        ],
      },
    ],
  };

  it("reads a tree as a sentence", () => {
    expect(conditionText(tree, subjects)).toBe(
      "Model contains Latitude, Memory is at least 8 GB, and (not (Device kind is Virtual machine or TPM present is no))",
    );
    expect(
      conditionSentence(
        "test",
        { kind: "test", variable: "Model", operator: "Contains", value: "Latitude" },
        subjects,
      ),
    ).toBe("Machines where Model contains Latitude go along Then.");
    expect(conditionSentence("when", null, subjects)).toBe("Runs on every machine.");
    expect(
      conditionSentence(
        "until",
        { kind: "test", variable: "LastExitCode", operator: "In", value: "0;3010" },
        subjects,
      ),
    ).toBe("Stops repeating once Exit code of the last script is one of 0 or 3010.");
    expect(
      conditionText(
        { kind: "test", variable: "SerialNumber", operator: "Exists", value: "" },
        subjects,
      ),
    ).toBe("Serial number has a value");
  });

  it("sums a tree up for a node's card", () => {
    expect(conditionSummary(tree, subjects)).toBe("4 conditions");
    expect(conditionSummary({ kind: "all", parts: [] }, subjects)).toBe("Always");
    expect(
      conditionSummary(
        {
          kind: "all",
          parts: [{ kind: "test", variable: "Subnet", operator: "Equals", value: "10.20.4.0/24" }],
        },
        subjects,
      ),
    ).toBe("Subnet equals 10.20.4.0/24");
  });
});

describe("a step's conditions of versions 1 and 2", () => {
  const conditions = [
    { variable: "Model", operator: "Equals" as const, value: "Latitude 7440" },
    { variable: "Phase", operator: "Equals" as const, value: "Windows" },
  ];
  const when: ConditionNode = {
    kind: "any",
    parts: [{ kind: "test", variable: "DeviceKind", operator: "Equals", value: "Laptop" }],
  };

  it("show as one tree with the step's when", () => {
    expect(legacyTree([], null)).toBeNull();
    expect(legacyTree([], when)).toBe(when);
    expect(legacyTree(conditions, null)).toEqual({
      kind: "all",
      parts: [
        { kind: "test", variable: "Model", operator: "Equals", value: "Latitude 7440" },
        { kind: "test", variable: "Phase", operator: "Equals", value: "Windows" },
      ],
    });
    expect(legacyTree(conditions, when)).toMatchObject({ kind: "all", parts: [{}, {}, when] });
  });

  it("name their places as the server's findings do", () => {
    expect(legacyPath(0, true, [1, 0])).toBe("when.parts[1].parts[0]");
    expect(legacyPath(0, true, [])).toBe("when");
    expect(legacyPath(2, false, [1])).toBe("conditions[1]");
    expect(legacyPath(2, true, [2, 0])).toBe("when.parts[0]");
    expect(legacyPath(2, true, [2])).toBe("when");
  });
});
