// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { i18n, type MessageDescriptor } from "@lingui/core";
import { msg } from "@lingui/core/macro";

import type {
  ConditionOperator,
  ScriptInterpreter,
  SequencePhase,
  SequenceStep,
  StepCondition,
  StepKind,
} from "./sequences";

const kindLabels: Record<StepKind, MessageDescriptor> = {
  partition: msg`Partition the disk`,
  applyImage: msg`Apply image`,
  injectDrivers: msg`Inject drivers`,
  writeUnattend: msg`Write the answer file`,
  joinDomain: msg`Join the domain`,
  runScript: msg`Run script`,
  reboot: msg`Restart`,
  writeRawImage: msg`Write raw disk image`,
  writeCloudInitSeed: msg`Write the cloud-init seed`,
};

// In the order a sequence usually has them.
export const stepKinds = Object.keys(kindLabels) as StepKind[];

// A run names its steps' kinds as text, so a kind this page does not know yet is shown as it is.
export function stepKindLabel(kind: string): string {
  return isStepKind(kind) ? i18n._(kindLabels[kind]) : kind;
}

export function isStepKind(kind: string): kind is StepKind {
  return Object.hasOwn(kindLabels, kind);
}

export function phaseLabel(phase: SequencePhase): string {
  return phase === "WindowsPE" ? i18n._(msg`Windows PE`) : i18n._(msg`Windows`);
}

// cloud-init's instance id, which runs its first-boot modules once per value, and the machine's name.
export const defaultMetaData =
  'instance-id: "{{SmbiosUuid}}"\nlocal-hostname: "{{ComputerName}}"\n';

// The placeholders the agent fills in the seed files, as {{Name}}.
export const seedPlaceholders = [
  "ComputerName",
  "Manufacturer",
  "Model",
  "SerialNumber",
  "SmbiosUuid",
  "MacAddress",
] as const;

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

const variableLabels: Record<(typeof machineVariables)[number], MessageDescriptor> = {
  Manufacturer: msg`Manufacturer`,
  Model: msg`Model`,
  SerialNumber: msg`Serial number`,
  SmbiosUuid: msg`SMBIOS UUID`,
  MacAddress: msg`MAC address`,
  ComputerName: msg`Computer name`,
  Phase: msg`Phase`,
};

export function variableLabel(variable: string): string {
  return Object.hasOwn(variableLabels, variable)
    ? i18n._(variableLabels[variable as (typeof machineVariables)[number]])
    : variable;
}

const operatorLabels: Record<ConditionOperator, MessageDescriptor> = {
  Equals: msg`equals`,
  NotEquals: msg`does not equal`,
  StartsWith: msg`starts with`,
  Contains: msg`contains`,
};

export const conditionOperators = Object.keys(operatorLabels) as ConditionOperator[];

export function operatorLabel(operator: ConditionOperator): string {
  return i18n._(operatorLabels[operator]);
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
    name: i18n._(kindLabels[kind]),
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
    case "writeRawImage":
      return { ...common, kind, imageId: EMPTY_ID };
    case "writeCloudInitSeed":
      return {
        ...common,
        kind,
        metaData: defaultMetaData,
        userData: "#cloud-config\n",
        networkConfig: null,
      };
  }
}
