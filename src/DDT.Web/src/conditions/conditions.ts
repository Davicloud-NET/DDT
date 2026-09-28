// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { i18n, type MessageDescriptor } from "@lingui/core";
import { msg, plural, t } from "@lingui/core/macro";

import { formattingLocale } from "@/i18n/i18n";
import type {
  ConditionGroupKind,
  ConditionNode,
  ConditionOperator,
  FactType,
  FactView,
  InputDeclaration,
  StepCondition,
  TestCondition,
  VariableDeclaration,
} from "@/sequences/sequences";
import { operatorLabel, operatorTakesValue } from "@/sequences/steps";

// What a condition can test and how: the subjects, each with the kind of value it holds, the operators that fit that
// kind, and the words a condition reads as. Shared by the flow builder and the rules.

// The server's MachineVariableNames.Catalogue, as GET /api/sequences/facts lists it, for a server that does not list it
// yet. In the order a page lists them.
export const factCatalogue: readonly FactView[] = (
  [
    ["Manufacturer", "Text"],
    ["Model", "Text"],
    ["FriendlyModel", "Text"],
    ["SerialNumber", "Text"],
    ["SmbiosUuid", "Text"],
    ["DeviceKind", "Text"],
    ["MacAddress", "Mac"],
    ["PrimaryMacAddress", "Mac"],
    ["ComputerName", "Text"],
    ["Phase", "Text"],
    ["MemoryMegabytes", "Number"],
    ["ProcessorName", "Text"],
    ["ProcessorCores", "Number"],
    ["LogicalProcessors", "Number"],
    ["TpmPresent", "YesNo"],
    ["TpmVersion", "Number"],
    ["SecureBootCapable", "YesNo"],
    ["SecureBootEnabled", "YesNo"],
    ["IPv4Address", "IPv4"],
    ["IPv4PrefixLength", "Number"],
    ["Subnet", "Text"],
    ["DefaultGateway", "IPv4"],
    ["DnsSuffix", "Text"],
    ["DhcpServer", "IPv4"],
    ["SystemVersion", "Text"],
    ["SystemFamily", "Text"],
    ["SystemSku", "Text"],
    ["AssetTag", "Text"],
    ["BaseboardProduct", "Text"],
    ["BiosVersion", "Text"],
    ["BiosDate", "Text"],
    ["LastStepFailed", "YesNo"],
    ["LastExitCode", "Number"],
  ] as const
).map(([name, type]) => ({
  name,
  type,
  changesDuringRun: name === "Phase" || name === "LastStepFailed" || name === "LastExitCode",
}));

// The run's own values: whether the last step failed, and the last script's exit code.
const runVariables: ReadonlySet<string> = new Set(["LastStepFailed", "LastExitCode"]);

const factLabels: Record<string, MessageDescriptor> = {
  Manufacturer: msg`Manufacturer`,
  Model: msg`Model`,
  FriendlyModel: msg`Friendly model`,
  SerialNumber: msg`Serial number`,
  SmbiosUuid: msg`SMBIOS UUID`,
  DeviceKind: msg`Device kind`,
  MacAddress: msg`MAC address`,
  PrimaryMacAddress: msg`Primary MAC address`,
  ComputerName: msg`Computer name`,
  Phase: msg`Phase`,
  MemoryMegabytes: msg`Memory`,
  ProcessorName: msg`Processor`,
  ProcessorCores: msg`Processor cores`,
  LogicalProcessors: msg`Logical processors`,
  TpmPresent: msg`TPM present`,
  TpmVersion: msg`TPM version`,
  SecureBootCapable: msg`Secure Boot capable`,
  SecureBootEnabled: msg`Secure Boot on`,
  IPv4Address: msg`IPv4 address`,
  IPv4PrefixLength: msg`Network prefix length`,
  Subnet: msg`Subnet`,
  DefaultGateway: msg`Default gateway`,
  DnsSuffix: msg`DNS suffix`,
  DhcpServer: msg`DHCP server`,
  SystemVersion: msg`System version`,
  SystemFamily: msg`System family`,
  SystemSku: msg`System SKU`,
  AssetTag: msg`Asset tag`,
  BaseboardProduct: msg`Baseboard`,
  BiosVersion: msg`BIOS version`,
  BiosDate: msg`BIOS date`,
  LastStepFailed: msg`The last step failed`,
  LastExitCode: msg`Exit code of the last script`,
};

export function factLabel(name: string): string {
  const descriptor = factLabels[name];

  return descriptor === undefined ? name : i18n._(descriptor);
}

// How the value of a subject is entered and read: text, a whole number, memory in GB (stored in MB), yes or no, an
// IPv4 address, a network such as 10.0.4.0/24, a MAC address, or one of a list.
export type ValueKind =
  "text" | "number" | "memory" | "yesNo" | "ipv4" | "network" | "mac" | "oneOf";

// A kind of value as the subject picker names it beside a subject.
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

function sameName(a: string, b: string): boolean {
  return a.toLowerCase() === b.toLowerCase();
}

// Every subject a condition of the sequence can test, in the picker's order: the machine's facts, the run's values,
// the names rules and machine roles set, then the sequence's own variables and inputs. A name comes once; an Account
// input sets no value, so it is not one.
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

  // The sequence's own come last, after the names from rules and machine roles.
  return [
    ...subjects.filter((subject) => subject.section !== "sequence"),
    ...values,
    ...sequence.sort((a, b) => own.indexOf(a.name) - own.indexOf(b.name)),
  ];
}

// The subject a condition names, ignoring case; a name no list has is tested as text, as the server does.
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

const operatorsByKind: Record<ValueKind, readonly ConditionOperator[]> = {
  text: [
    "Equals",
    "NotEquals",
    "Contains",
    "NotContains",
    "StartsWith",
    "EndsWith",
    "Matches",
    "In",
    "Exists",
    "NotExists",
  ],
  oneOf: ["Equals", "NotEquals", "In", "Exists", "NotExists"],
  number: [
    "Equals",
    "NotEquals",
    "GreaterOrEqual",
    "Greater",
    "LessOrEqual",
    "Less",
    "In",
    "Exists",
    "NotExists",
  ],
  memory: ["GreaterOrEqual", "Greater", "LessOrEqual", "Less", "Equals", "NotEquals"],
  yesNo: ["Equals", "NotEquals", "Exists", "NotExists"],
  ipv4: ["InSubnet", "Equals", "NotEquals", "StartsWith", "In", "Exists", "NotExists"],
  network: ["Equals", "NotEquals", "StartsWith", "In", "Exists", "NotExists"],
  mac: ["Equals", "NotEquals", "StartsWith", "In", "Exists", "NotExists"],
};

// The operators that fit a kind of value, the one a new condition takes first.
export function operatorsFor(kind: ValueKind): readonly ConditionOperator[] {
  return operatorsByKind[kind];
}

// What an operator says for a kind: yes or no reads "is" and "is not".
export function operatorText(operator: ConditionOperator, kind: ValueKind): string {
  if (kind === "yesNo" && operator === "Equals") {
    return t`is`;
  }

  if (kind === "yesNo" && operator === "NotEquals") {
    return t`is not`;
  }

  if (kind === "oneOf" && operator === "Equals") {
    return t`is`;
  }

  if (kind === "oneOf" && operator === "NotEquals") {
    return t`is not`;
  }

  return operatorLabel(operator);
}

export { operatorTakesValue };

// A list of values, as In takes them: separated by semicolons.
export function listOf(value: string): string[] {
  return value
    .split(";")
    .map((item) => item.trim())
    .filter((item) => item !== "");
}

// Memory is stored in MB and entered in GB, to two places.
export function gigabytesOf(megabytes: string): string {
  const number = Number(megabytes);

  if (megabytes.trim() === "" || !Number.isFinite(number)) {
    return megabytes;
  }

  return String(Math.round((number / 1024) * 100) / 100);
}

export function megabytesOf(gigabytes: string): string {
  const typed = gigabytes.trim().replace(",", ".");
  const number = Number(typed);

  if (typed === "" || !Number.isFinite(number)) {
    return gigabytes;
  }

  return String(Math.round(number * 1024));
}

const ipv4Pattern = /^(25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)(\.(25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)){3}$/;

export function isIpv4(value: string): boolean {
  return ipv4Pattern.test(value.trim());
}

export function isNetwork(value: string): boolean {
  const [address = "", prefix, ...rest] = value.trim().split("/");

  return (
    rest.length === 0 &&
    prefix !== undefined &&
    /^\d{1,2}$/.test(prefix) &&
    Number(prefix) <= 32 &&
    isIpv4(address)
  );
}

// Whole bytes of hex digits, written with or without colons, dashes or dots.
export function isMac(value: string, part: boolean): boolean {
  const digits = value.replace(/[:\-. ]/g, "");

  return (
    /^[0-9A-Fa-f]+$/.test(digits) &&
    digits.length % 2 === 0 &&
    (part ? digits.length <= 12 : digits.length === 12)
  );
}

// What is wrong with a value as it is typed, for the field to say before the server does; null when nothing is.
export function valueProblem(
  kind: ValueKind,
  operator: ConditionOperator,
  value: string,
): string | null {
  if (!operatorTakesValue(operator) || value.trim() === "") {
    return null;
  }

  const items = operator === "In" ? listOf(value) : [value.trim()];
  const every = (check: (item: string) => boolean) => items.every(check);

  if (operator === "InSubnet") {
    return every(isNetwork) ? null : t`Enter a network such as 10.20.0.0/16.`;
  }

  switch (kind) {
    case "number":
    case "memory":
      return every((item) => Number.isFinite(Number(item.replace(",", "."))))
        ? null
        : t`Enter a number.`;
    case "ipv4":
      return operator === "StartsWith" || every(isIpv4)
        ? null
        : t`Enter an IPv4 address such as 10.0.4.51.`;
    case "network":
      return operator === "StartsWith" || every(isNetwork)
        ? null
        : t`Enter a network such as 10.20.4.0/24.`;
    case "mac":
      return every((item) => isMac(item, operator === "StartsWith"))
        ? null
        : t`Enter a MAC address such as 00:15:5D:01:02:03.`;
    default:
      return null;
  }
}

// A new test of the subject, with the first operator that fits it and an empty value.
export function newTestOf(subject: Subject): TestCondition {
  const operator = operatorsFor(subject.kind)[0] ?? "Equals";

  return {
    kind: "test",
    variable: subject.name,
    operator,
    value:
      subject.kind === "yesNo"
        ? "true"
        : subject.kind === "oneOf"
          ? (subject.choices[0]?.value ?? "")
          : "",
  };
}

// The test after its subject changed: the operator stays where it fits the new kind, and the value where the kind
// stayed the same.
export function withSubject(test: TestCondition, from: Subject, to: Subject): TestCondition {
  const fits = operatorsFor(to.kind).includes(test.operator);
  const fresh = newTestOf(to);

  return {
    ...test,
    variable: to.name,
    operator: fits ? test.operator : fresh.operator,
    value: from.kind === to.kind && to.kind !== "oneOf" ? test.value : fresh.value,
  };
}

// A value as a sentence says it: yes or no, memory in GB, the label of a choice, a list joined.
export function valueText(subject: Subject, value: string): string {
  const one = (item: string) => {
    switch (subject.kind) {
      case "yesNo": {
        const lower = item.trim().toLowerCase();

        return ["true", "yes", "1"].includes(lower)
          ? t`yes`
          : ["false", "no", "0"].includes(lower)
            ? t`no`
            : item;
      }
      case "memory": {
        const size = gigabytesOf(item);

        return t`${size} GB`;
      }
      case "oneOf":
        return subject.choices.find((choice) => sameName(choice.value, item))?.label ?? item;
      default:
        return item;
    }
  };
  const items = listOf(value);

  return items.length > 1
    ? new Intl.ListFormat(formattingLocale(), { type: "disjunction" }).format(items.map(one))
    : one(value.trim());
}

export function testText(test: TestCondition, subjects: readonly Subject[]): string {
  const subject = subjectFor(subjects, test.variable);
  const label = subject.label;
  const operator = operatorText(test.operator, subject.kind);

  if (!operatorTakesValue(test.operator)) {
    return t`${label} ${operator}`;
  }

  const value = valueText(subject, test.value);

  return t`${label} ${operator} ${value}`;
}

// A condition in words, such as "Model contains Latitude and Memory is at least 8 GB". Groups inside groups are in
// brackets.
export function conditionText(node: ConditionNode, subjects: readonly Subject[]): string {
  if (node.kind === "test") {
    return testText(node, subjects);
  }

  const parts = node.parts.map((part) =>
    part.kind === "test" || part.parts.length <= 1
      ? conditionText(part, subjects)
      : `(${conditionText(part, subjects)})`,
  );
  const locale = formattingLocale();

  if (parts.length === 0) {
    return node.kind === "any" ? t`nothing` : t`always`;
  }

  if (node.kind === "all") {
    return new Intl.ListFormat(locale, { type: "conjunction" }).format(parts);
  }

  const either = new Intl.ListFormat(locale, { type: "disjunction" }).format(parts);

  return node.kind === "any" ? either : t`not (${either})`;
}

// Every test of a tree.
function testCount(node: ConditionNode | null): number {
  return node === null
    ? 0
    : node.kind === "test"
      ? 1
      : node.parts.reduce((sum, part) => sum + testCount(part), 0);
}

// A condition short enough for a node's card: its one test in words, or how many tests it has.
export function conditionSummary(node: ConditionNode | null, subjects: readonly Subject[]): string {
  const count = testCount(node);

  if (node === null || count === 0) {
    return t`Always`;
  }

  return count === 1 || node.kind === "test"
    ? conditionText(node, subjects)
    : plural(count, { one: "# condition", other: "# conditions" });
}

export type ConditionUse = "when" | "test" | "until" | "rule";

// What a condition means where it is, as the builder says it under its rows.
export function conditionSentence(
  use: ConditionUse,
  node: ConditionNode | null,
  subjects: readonly Subject[],
): string {
  const empty = node === null || testCount(node) === 0;
  const condition = empty ? "" : conditionText(node, subjects);

  switch (use) {
    case "when":
      return empty ? t`Runs on every machine.` : t`Runs only where ${condition}.`;
    case "test":
      return empty
        ? t`Every machine goes along Then.`
        : t`Machines where ${condition} go along Then.`;
    case "until":
      return empty ? t`Stops after the first time.` : t`Stops repeating once ${condition}.`;
    case "rule":
      return empty ? t`Matches every machine.` : t`Matches machines where ${condition}.`;
  }
}

// How deep a condition may go: a group within a group, four levels in all.
export const MAX_CONDITION_DEPTH = 4;

export const groupKinds: readonly ConditionGroupKind[] = ["all", "any", "none"];

// A group as its choice in the builder says it.
export function groupLabel(kind: ConditionGroupKind): string {
  switch (kind) {
    case "all":
      return t`All of these hold`;
    case "any":
      return t`At least one of these holds`;
    case "none":
      return t`None of these hold`;
  }
}

// A step's conditions of versions 1 and 2 as a tree, with its when beside them, so the builder shows both as one.
export function legacyTree(
  conditions: readonly StepCondition[],
  when: ConditionNode | null | undefined,
): ConditionNode | null {
  const tests: ConditionNode[] = conditions.map((condition) => ({ kind: "test", ...condition }));

  if (tests.length === 0) {
    return when ?? null;
  }

  return { kind: "all", parts: when === null || when === undefined ? tests : [...tests, when] };
}

// Where a place of the tree legacyTree made is in the step: its conditions for the tests that came from them, its
// when for the rest, such as "conditions[1]" or "when.parts[0]".
export function legacyPath(legacyCount: number, hasWhen: boolean, path: readonly number[]): string {
  const parts = (from: readonly number[]) =>
    from.map((index) => `.parts[${String(index)}]`).join("");

  if (legacyCount === 0) {
    return `when${parts(path)}`;
  }

  const [first, ...rest] = path;

  if (first === undefined) {
    return "conditions";
  }

  if (first < legacyCount) {
    return `conditions[${String(first)}]`;
  }

  return hasWhen ? `when${parts(rest)}` : "conditions";
}

// A machine to preview templates for, with a value for every fact, as the builder shows it.
export const sampleMachine: Readonly<Record<string, string>> = {
  Manufacturer: "LENOVO",
  Model: "21HD003GGE",
  FriendlyModel: "ThinkPad T14 Gen 4",
  SerialNumber: "PF4K2Z7Q",
  SmbiosUuid: "4C4C4544-0052-3710-8047-B4C04F4B5A31",
  DeviceKind: "Laptop",
  MacAddress: "8C:16:45:A0:B2:C4",
  PrimaryMacAddress: "8C:16:45:A0:B2:C4",
  ComputerName: "PC-042",
  Phase: "WindowsPE",
  MemoryMegabytes: "32768",
  ProcessorName: "13th Gen Intel(R) Core(TM) i7-1365U",
  ProcessorCores: "10",
  LogicalProcessors: "12",
  TpmPresent: "true",
  TpmVersion: "2.0",
  SecureBootCapable: "true",
  SecureBootEnabled: "true",
  IPv4Address: "10.20.4.51",
  IPv4PrefixLength: "24",
  Subnet: "10.20.4.0/24",
  DefaultGateway: "10.20.4.1",
  DnsSuffix: "berlin.corp.example",
  DhcpServer: "10.20.0.10",
  SystemVersion: "ThinkPad T14 Gen 4",
  SystemFamily: "ThinkPad T14 Gen 4",
  SystemSku: "LENOVO_MT_21HD_BU_Think_FM_ThinkPad T14 Gen 4",
  AssetTag: "IT-004211",
  BaseboardProduct: "21HD003GGE",
  BiosVersion: "R2FET58W (1.38 )",
  BiosDate: "2026-03-14",
  LastStepFailed: "false",
  LastExitCode: "0",
};
