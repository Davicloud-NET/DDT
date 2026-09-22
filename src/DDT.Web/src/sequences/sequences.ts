// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { queryOptions } from "@tanstack/react-query";

import { apiDelete, apiGet, apiPost } from "@/lib/api";

export type SequencePhase = "WindowsPE" | "Windows";

export type ConditionOperator = "Equals" | "NotEquals" | "StartsWith" | "Contains";

export type ScriptInterpreter = "Cmd" | "PowerShell";

export type StepState = "Pending" | "Running" | "Done" | "Skipped" | "Failed";

// variable is one of the server's MachineVariableNames, such as "Model", "MacAddress" or "Phase".
export interface StepCondition {
  variable: string;
  operator: ConditionOperator;
  value: string;
}

// id stays the same across edits: run state, reports and problems name a step by it. The step runs only when
// every condition holds.
interface StepBase {
  id: string;
  name: string;
  conditions: StepCondition[];
  continueOnError: boolean;
  rebootAfter: boolean;
}

export interface PartitionStep extends StepBase {
  kind: "partition";
  systemPartitionMegabytes: number;
  recoveryPartitionMegabytes: number;
}

export interface ApplyImageStep extends StepBase {
  kind: "applyImage";
  imageId: string;
}

// The server picks the driver packages by the machine's model. requireMatch fails the step when it has none.
export interface InjectDriversStep extends StepBase {
  kind: "injectDrivers";
  requireMatch: boolean;
}

// Null settings take the server's defaults.
export interface WriteUnattendStep extends StepBase {
  kind: "writeUnattend";
  timeZone: string | null;
  locale: string | null;
  keyboard: string | null;
  localAdministrator: boolean;
}

// Null takes the configured default.
export interface JoinDomainStep extends StepBase {
  kind: "joinDomain";
  organizationalUnit: string | null;
}

// packageId names a Files package that is extracted and becomes the script's working directory.
export interface RunScriptStep extends StepBase {
  kind: "runScript";
  phase: SequencePhase;
  interpreter: ScriptInterpreter;
  script: string;
  packageId: string | null;
  timeoutMinutes: number;
  successExitCodes: number[];
  rebootExitCodes: number[];
}

export interface RebootStep extends StepBase {
  kind: "reboot";
}

// The kind values are stored in sequences and run snapshots, so they never change.
export type SequenceStep =
  | PartitionStep
  | ApplyImageStep
  | InjectDriversStep
  | WriteUnattendStep
  | JoinDomainStep
  | RunScriptStep
  | RebootStep;

export type StepKind = SequenceStep["kind"];

// version is the document schema, raised when a step kind is added.
export interface SequenceDefinition {
  version: number;
  steps: SequenceStep[];
}

// A sequence with problemCount above zero is kept as a draft and cannot run. The facts let a dialog say what
// running it does without loading the whole document. needsComputerName: the sequence joins the domain.
export interface SequenceSummary {
  id: string;
  name: string;
  description: string | null;
  revision: number;
  stepCount: number;
  problemCount: number;
  warningCount: number;
  erasesDisk: boolean;
  needsComputerName: boolean;
  continuesInWindows: boolean;
  updatedUtc: string;
  updatedBy: string | null;
}

// SequenceDefinition.CurrentVersion: the document schema this page writes.
export const SEQUENCE_VERSION = 1;

// stepId is null for a problem of the whole sequence. field is the camelCase name within the step, such as
// "script" or "conditions[1].value". Every problem keeps the sequence from running; warnings do not.
export interface SequenceProblem {
  stepId: string | null;
  field: string | null;
  message: string;
}

// stepPhases holds the phase each step runs in, in step order, as the engine decides it. Problems and warnings
// are worked out on every read, because a deleted image or a changed setting changes them.
export interface SequenceView {
  id: string;
  name: string;
  description: string | null;
  revision: number;
  definition: SequenceDefinition;
  stepPhases: SequencePhase[];
  problems: SequenceProblem[];
  warnings: SequenceProblem[];
  updatedUtc: string;
  updatedBy: string | null;
}

// A starting point for a new sequence.
export interface SequenceTemplate {
  key: string;
  name: string;
  description: string;
  definition: SequenceDefinition;
}

export interface CreateSequenceRequest {
  name: string;
  description: string | null;
  definition: SequenceDefinition;
}

export const sequencesQuery = queryOptions({
  queryKey: ["sequences"],
  queryFn: () => apiGet<SequenceSummary[]>("/api/sequences"),
});

// Only administrators may read the templates.
export const templatesQuery = queryOptions({
  queryKey: ["sequence-templates"],
  queryFn: () => apiGet<SequenceTemplate[]>("/api/sequences/templates"),
  staleTime: 5 * 60_000,
});

// A root of its own, so a change of the list never reads an open editor's document again.
export function sequenceQuery(id: string) {
  return queryOptions({
    queryKey: ["sequence", id],
    queryFn: () => apiGet<SequenceView>(`/api/sequences/${id}`),
  });
}

// The server refuses to assign or start a sequence with problems.
export function canRun(sequence: SequenceSummary): boolean {
  return sequence.problemCount === 0;
}

export function createSequence(request: CreateSequenceRequest): Promise<SequenceView> {
  return apiPost<SequenceView>("/api/sequences", request);
}

// Refused with 409 while a rule chooses the sequence.
export function deleteSequence(id: string): Promise<void> {
  return apiDelete(`/api/sequences/${id}`);
}
