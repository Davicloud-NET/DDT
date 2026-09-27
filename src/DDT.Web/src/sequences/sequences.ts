// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { queryOptions } from "@tanstack/react-query";

import { apiDelete, apiGet, apiPost, apiPut } from "@/lib/api";
import type { ServerArguments } from "@/lib/serverText";
import type { ImageBootCapability } from "@/images/images";

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

export interface WriteRawImageStep extends StepBase {
  kind: "writeRawImage";
  imageId: string;
}

// The seed files may use placeholders such as {{ComputerName}}. networkConfig is left out when null.
export interface WriteCloudInitSeedStep extends StepBase {
  kind: "writeCloudInitSeed";
  metaData: string;
  userData: string;
  networkConfig: string | null;
}

// The kind values are stored in sequences and run snapshots, so they never change.
export type SequenceStep =
  | PartitionStep
  | ApplyImageStep
  | InjectDriversStep
  | WriteUnattendStep
  | JoinDomainStep
  | RunScriptStep
  | RebootStep
  | WriteRawImageStep
  | WriteCloudInitSeedStep;

export type StepKind = SequenceStep["kind"];

// version is the document schema, raised when a step kind is added.
export interface SequenceDefinition {
  version: number;
  steps: SequenceStep[];
}

// A sequence with problemCount above zero is kept as a draft and cannot run. The facts let a dialog say what
// running it does without loading the whole document. needsComputerName: the sequence joins the domain under the
// machine's name, or puts it in a cloud-init seed.
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
  // The raw disk image the sequence writes, if any, whether it starts with Secure Boot on, and which of Microsoft's
  // third-party UEFI CAs its boot file is signed under, written as the machine's trustedUefiCas are.
  rawImageName: string | null;
  rawImageBootCapability: ImageBootCapability | null;
  rawImageSignedUnder: string | null;
}

// SequenceDefinition.CurrentVersion: the document schema this page writes.
// The highest version this page knows. The server stores each sequence with the lowest version its kinds need.
export const SEQUENCE_VERSION = 2;

// stepId is null for a problem of the whole sequence. field is the camelCase name within the step, such as
// "script" or "conditions[1].value". Every problem keeps the sequence from running; warnings do not. message is
// the server's English, code and args the same sentence to say in the person's language (findingText).
export interface SequenceProblem {
  stepId: string | null;
  field: string | null;
  message: string;
  code?: string | null;
  args?: ServerArguments | null;
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
// name and description are the server's English; their codes and values say them in the person's language.
export interface SequenceTemplate {
  key: string;
  name: string;
  description: string;
  definition: SequenceDefinition;
  nameCode?: string | null;
  nameArgs?: ServerArguments | null;
  descriptionCode?: string | null;
  descriptionArgs?: ServerArguments | null;
}

export interface CreateSequenceRequest {
  name: string;
  description: string | null;
  definition: SequenceDefinition;
}

// revision is the one the page last read. A save over a newer one is refused with 409 and the current view.
export interface SaveSequenceRequest {
  revision: number;
  name: string;
  description: string | null;
  definition: SequenceDefinition;
}

// The server's SequenceChangedEvent. revision is null when the sequence was deleted.
export interface SequenceChanged {
  id: string;
  revision: number | null;
  changedBy: string | null;
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
export const sequenceDocumentsKey = ["sequence"] as const;

export function sequenceQuery(id: string) {
  return queryOptions({
    queryKey: [...sequenceDocumentsKey, id],
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

export function saveSequence(
  id: string,
  request: SaveSequenceRequest,
  keepalive = false,
): Promise<SequenceView> {
  return apiPut<SequenceView>(`/api/sequences/${id}`, request, { keepalive });
}

// Refused with 409 while a rule chooses the sequence.
export function deleteSequence(id: string): Promise<void> {
  return apiDelete(`/api/sequences/${id}`);
}
