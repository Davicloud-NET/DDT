// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { i18n, type MessageDescriptor } from "@lingui/core";
import { msg, t } from "@lingui/core/macro";

import { factLabel } from "@/machines/facts";
import type { FactType, FactView } from "@/sequences/sequenceConditions";
import type { InputDeclaration, VariableDeclaration } from "@/sequences/sequences";

// The run's own values: whether the last step failed, and the last script's exit code.
const runVariables: ReadonlySet<string> = new Set(["LastStepFailed", "LastExitCode"]);

// How the value of a subject is entered and read: text, a whole number, memory in GB (stored in MB), yes or no, an
// IPv4 address, a network such as 10.0.4.0/24, a MAC address, or one of a list.
export type ValueKind =
  "text" | "number" | "memory" | "yesNo" | "ipv4" | "network" | "mac" | "oneOf";

// The name of a value kind, as the subject picker shows it beside a subject.
export function valueKindLabel(kind: ValueKind): string {
  switch (kind) {
    case "text":
      return t`text`;
    case "number":
      return t`number`;
    case "memory":
      return t`memory`;
    case "yesNo":
      return t`yes or no`;
    case "ipv4":
      return t`address`;
    case "network":
      return t`network`;
    case "mac":
      return t`MAC`;
    case "oneOf":
      return t`one of`;
  }
}

export interface ValueChoice {
  value: string;
  label: string;
}

// Machine: the facts. Run: what the run itself sets. Values: names that rules and machine roles set. Sequence: the
// sequence's variables and inputs.
export type SubjectSection = "machine" | "run" | "values" | "sequence";

export interface Subject {
  name: string;
  label: string;
  section: SubjectSection;
  kind: ValueKind;
  choices: ValueChoice[];
  // Beside the name in the picker, such as where an input is asked.
  note: string | null;
}

const deviceKinds: readonly [string, MessageDescriptor][] = [
  ["Laptop", msg`Laptop`],
  ["Desktop", msg`Desktop`],
  ["Tablet", msg`Tablet`],
  ["Server", msg`Server`],
  ["Virtual", msg`Virtual machine`],
  ["Unknown", msg`Unknown`],
];

function kindOfFact(fact: FactView): ValueKind {
  switch (fact.name) {
    case "MemoryMegabytes":
      return "memory";
    case "DeviceKind":
    case "Phase":
      return "oneOf";
    case "Subnet":
      return "network";
  }

  const kinds: Record<FactType, ValueKind> = {
    Text: "text",
    Number: "number",
    YesNo: "yesNo",
    IPv4: "ipv4",
    Mac: "mac",
  };

  return kinds[fact.type];
}

function choicesOfFact(name: string): ValueChoice[] {
  if (name === "DeviceKind") {
    return deviceKinds.map(([value, label]) => ({ value, label: i18n._(label) }));
  }

  if (name === "Phase") {
    return [
      { value: "WindowsPE", label: t`Windows PE` },
      { value: "Windows", label: t`Windows` },
    ];
  }

  return [];
}

export function factSubjects(facts: readonly FactView[]): Subject[] {
  return facts.map((fact) => ({
    name: fact.name,
    label: factLabel(fact.name),
    section: runVariables.has(fact.name) ? "run" : "machine",
    kind: kindOfFact(fact),
    choices: choicesOfFact(fact.name),
    note: null,
  }));
}

function askedAt(input: InputDeclaration): string {
  switch (input.askAt) {
    case "Web":
      return t`asked on the web`;
    case "Machine":
      return t`asked at the machine`;
    case "Both":
      return t`asked on the web or at the machine`;
  }
}

function inputSubject(input: InputDeclaration): Subject {
  const choices = input.choices.map((choice) => ({
    value: choice.value,
    label: choice.label ?? choice.value,
  }));

  return {
    name: input.name,
    label: input.name,
    section: "sequence",
    kind: input.kind === "YesNo" ? "yesNo" : input.kind === "Choice" ? "oneOf" : "text",
    choices,
    note: askedAt(input),
  };
}

export function sameName(a: string, b: string): boolean {
  return a.toLowerCase() === b.toLowerCase();
}

// Every subject a condition in the sequence can test, each name once, in the picker's order: facts, the run's values,
// names that rules and roles set, then the sequence's variables and inputs. An Account input doesn't set a value, so
// it isn't a subject.
export function subjectsOf({
  facts,
  valueNames,
  variables,
  inputs,
}: {
  facts: readonly FactView[];
  valueNames: readonly string[];
  variables: readonly VariableDeclaration[];
  inputs: readonly InputDeclaration[];
}): Subject[] {
  const subjects = factSubjects(facts);
  const add = (subject: Subject) => {
    if (!subjects.some((other) => sameName(other.name, subject.name))) {
      subjects.push(subject);
    }
  };
  const own = [...variables.map((variable) => variable.name), ...inputs.map((input) => input.name)];

  for (const input of inputs) {
    if (input.kind !== "Account") {
      add(inputSubject(input));
    }
  }

  for (const variable of variables) {
    add({
      name: variable.name,
      label: variable.name,
      section: "sequence",
      kind: "text",
      choices: [],
      note: null,
    });
  }

  const sequence = subjects.filter((subject) => subject.section === "sequence");
  const values = valueNames
    .filter((name) => !own.some((other) => sameName(other, name)))
    .filter((name) => !subjects.some((other) => sameName(other.name, name)))
    .filter((name, index, all) => all.findIndex((other) => sameName(other, name)) === index)
    .map((name): Subject => ({
      name,
      label: name,
      section: "values",
      kind: "text",
      choices: [],
      note: null,
    }));

  return [
    ...subjects.filter((subject) => subject.section !== "sequence"),
    ...values,
    ...sequence.sort((a, b) => own.indexOf(a.name) - own.indexOf(b.name)),
  ];
}

// The subject a condition names, ignoring case. Like on the server, a name that's in no list is tested as text.
export function subjectFor(subjects: readonly Subject[], name: string): Subject {
  return (
    subjects.find((subject) => sameName(subject.name, name)) ?? {
      name,
      label: name === "" ? t`Choose what to test` : name,
      section: "values",
      kind: "text",
      choices: [],
      note: null,
    }
  );
}
