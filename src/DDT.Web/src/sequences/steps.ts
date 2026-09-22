// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type {
  ConditionOperator,
  ScriptInterpreter,
  SequencePhase,
  SequenceStep,
  StepCondition,
  StepKind,
} from "./sequences";

const kindLabels: Record<StepKind, string> = {
  partition: "Partition the disk",
  applyImage: "Apply image",
  injectDrivers: "Inject drivers",
  writeUnattend: "Write the answer file",
  joinDomain: "Join the domain",
  runScript: "Run script",
  reboot: "Restart",
};

// In the order a sequence usually has them.
export const stepKinds = Object.keys(kindLabels) as StepKind[];

// A run names its steps' kinds as text, so a kind this page does not know yet is shown as it is.
export function stepKindLabel(kind: string): string {
  return isStepKind(kind) ? kindLabels[kind] : kind;
}

export function isStepKind(kind: string): kind is StepKind {
  return Object.hasOwn(kindLabels, kind);
}

export function phaseLabel(phase: SequencePhase): string {
  return phase === "WindowsPE" ? "Windows PE" : "Windows";
}

// Guid.Empty, which the server reads as "no image chosen yet".
export const EMPTY_ID = "00000000-0000-0000-0000-000000000000";

// The server's MachineVariableNames.All, which conditions compare with.
export const machineVariables = [
  "Manufacturer",
  "Model",
  "SerialNumber",
  "SmbiosUuid",
  "MacAddress",
  "ComputerName",
  "Phase",
] as const;

const variableLabels: Record<(typeof machineVariables)[number], string> = {
  Manufacturer: "Manufacturer",
  Model: "Model",
  SerialNumber: "Serial number",
  SmbiosUuid: "SMBIOS UUID",
  MacAddress: "MAC address",
  ComputerName: "Computer name",
  Phase: "Phase",
};

export function variableLabel(variable: string): string {
  return Object.hasOwn(variableLabels, variable)
    ? variableLabels[variable as (typeof machineVariables)[number]]
    : variable;
}

const operatorLabels: Record<ConditionOperator, string> = {
  Equals: "equals",
  NotEquals: "does not equal",
  StartsWith: "starts with",
  Contains: "contains",
};

export const conditionOperators = Object.keys(operatorLabels) as ConditionOperator[];

export function operatorLabel(operator: ConditionOperator): string {
  return operatorLabels[operator];
}

export const interpreters: ScriptInterpreter[] = ["Cmd", "PowerShell"];

export function interpreterLabel(interpreter: ScriptInterpreter): string {
  return interpreter === "Cmd" ? "cmd" : "PowerShell";
}

// The server reads a sequence's numbers as Int32 and refuses the whole document when one is larger.
export function isInt32(value: number): boolean {
  return Number.isInteger(value) && value >= -2_147_483_648 && value <= 2_147_483_647;
}

// Exit codes typed as whole numbers separated by commas or spaces; null when the text holds something else.
export function parseCodes(text: string): number[] | null {
  const parts = text.split(/[\s,;]+/).filter((part) => part !== "");

  return parts.every((part) => /^-?\d{1,10}$/.test(part) && isInt32(Number(part)))
    ? parts.map(Number)
    : null;
}

export function newCondition(): StepCondition {
  return { variable: "Model", operator: "Equals", value: "" };
}

// A new step with the server's defaults, named after its kind.
export function newStep(kind: StepKind, id: string): SequenceStep {
  const common = {
    id,
    name: kindLabels[kind],
    conditions: [],
    continueOnError: false,
    rebootAfter: false,
  };

  switch (kind) {
    case "partition":
      return {
        ...common,
        kind,
        systemPartitionMegabytes: 300,
        recoveryPartitionMegabytes: 1024,
      };
    case "applyImage":
      return { ...common, kind, imageId: EMPTY_ID };
    case "injectDrivers":
      return { ...common, kind, requireMatch: false };
    case "writeUnattend":
      return {
        ...common,
        kind,
        timeZone: null,
        locale: null,
        keyboard: null,
        localAdministrator: false,
      };
    case "joinDomain":
      return { ...common, kind, organizationalUnit: null };
    case "runScript":
      return {
        ...common,
        kind,
        phase: "WindowsPE",
        interpreter: "Cmd",
        script: "",
        packageId: null,
        timeoutMinutes: 60,
        successExitCodes: [0],
        rebootExitCodes: [3010],
      };
    case "reboot":
      return { ...common, kind };
  }
}
