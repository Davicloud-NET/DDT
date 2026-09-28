// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { queryOptions } from "@tanstack/react-query";

import { apiDelete, apiGet, apiPost, apiPut } from "@/lib/api";
import type { ServerArguments } from "@/lib/serverText";
import type { ImageBootCapability } from "@/images/images";

import type { ConditionNode, StepCondition } from "./sequenceConditions";

export type SequencePhase = "WindowsPE" | "Windows";

export type ScriptInterpreter = "Cmd" | "PowerShell";

export type StepState = "Pending" | "Running" | "Done" | "Skipped" | "Failed";

// The account a step uses. It's exactly one of a stored account or an Account input the sequence declares, whose
// answer is kept for that one run. It never holds a password.
export interface AccountReference {
  accountId: string | null;
  input: string | null;
}

// A share DDT connects with the account while the step runs. path is a template whose host comes only from values
// fixed when the run starts.
export interface ShareConnection {
  path: string;
  account: AccountReference;
}

// id stays the same across edits, because run state, reports and problems name a step by it. The step only runs if
// every condition holds and when holds too. Version 3 members are left out of the JSON while unset.
interface StepBase {
  id: string;
  name: string;
  conditions: StepCondition[];
  continueOnError: boolean;
  rebootAfter: boolean;
  when?: ConditionNode | null;
  // The shares DDT connects before the step runs and disconnects after it.
  shares?: ShareConnection[] | null;
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

// The server picks the driver packages by the machine's model. requireMatch fails the step if there are none for it.
export interface InjectDriversStep extends StepBase {
  kind: "injectDrivers";
  requireMatch: boolean;
}

// Null settings take the server's defaults. From version 3 they are templates.
export interface WriteUnattendStep extends StepBase {
  kind: "writeUnattend";
  timeZone: string | null;
  locale: string | null;
  keyboard: string | null;
  localAdministrator: boolean;
}

// Null takes the configured default. From version 3, organizationalUnit is a template. account (version 3) is the
// account that joins, and it also names the domain. Without one, the configured join account is used.
export interface JoinDomainStep extends StepBase {
  kind: "joinDomain";
  organizationalUnit: string | null;
  account?: AccountReference | null;
}

// packageId names a Files package that's extracted and becomes the script's working directory. runAs (version 3,
// Windows phase only) runs the script as this account instead of SYSTEM. The script never sees the password.
export interface RunScriptStep extends StepBase {
  kind: "runScript";
  phase: SequencePhase;
  interpreter: ScriptInterpreter;
  script: string;
  packageId: string | null;
  timeoutMinutes: number;
  successExitCodes: number[];
  rebootExitCodes: number[];
  runAs?: AccountReference | null;
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

// Runs its steps in order. Its conditions and continueOnError apply to all of them. It doesn't connect shares itself.
// A share belongs to the step inside that needs it, for as long as that step runs.
export interface GroupStep extends StepBase {
  kind: "group";
  steps: SequenceStep[];
}

// Runs then if test holds, and else if it doesn't. Both paths meet again after it. An else-if is an IF in else.
export interface IfStep extends StepBase {
  kind: "if";
  test: ConditionNode;
  then: SequenceStep[];
  else: SequenceStep[];
}

// Runs steps, then tests until, and repeats until it holds, at most maxTimes times (1 to 100). If until still doesn't
// hold after the last time, the Repeat fails, unless goOnAtLimit lets the run continue.
export interface RepeatStep extends StepBase {
  kind: "repeat";
  steps: SequenceStep[];
  until: ConditionNode;
  maxTimes: number;
  goOnAtLimit: boolean;
}

// Sets a variable the sequence declares with setBySteps. value is a template, worked out by the agent.
export interface SetVariableStep extends StepBase {
  kind: "setVariable";
  variable: string;
  value: string;
}

// Waits until someone continues the run, or until continueAfterMinutes (1 to 1440) have passed. Null waits as long as
// it takes. message is a template.
export interface PauseStep extends StepBase {
  kind: "pause";
  message: string;
  continueAfterMinutes: number | null;
}

// The kind values are stored in sequences and run snapshots, so they never change. A step is a node of the
// sequence's tree: a leaf, or a container whose bodies hold more nodes.
export type SequenceStep =
  | PartitionStep
  | ApplyImageStep
  | InjectDriversStep
  | WriteUnattendStep
  | JoinDomainStep
  | RunScriptStep
  | RebootStep
  | WriteRawImageStep
  | WriteCloudInitSeedStep
  | GroupStep
  | IfStep
  | RepeatStep
  | SetVariableStep
  | PauseStep;

export type StepKind = SequenceStep["kind"];

export type ContainerStep = GroupStep | IfStep | RepeatStep;

export type ContainerKind = ContainerStep["kind"];

// A value the sequence uses, such as ComputerName or Office. default is a template. Only a variable with setBySteps
// may be changed by a Set variable step or a script's output during the run.
export interface VariableDeclaration {
  name: string;
  default: string | null;
  description: string | null;
  setBySteps: boolean;
}

export type InputKind = "Text" | "Choice" | "MultiChoice" | "YesNo" | "Account";

export type InputAsk = "Both" | "Web" | "Machine";

// label is what the person sees, and value is what the answer sets. A null label shows the value.
export interface InputChoice {
  value: string;
  label: string | null;
}

// Where an Account input's account may be used: the domain it joins, the share hosts it may connect to, and
// whether a script may run as it.
export interface AccountDestination {
  domain: string | null;
  hosts: string[];
  runAs: boolean;
}

// A question asked before the run starts, on the web, at the machine or both. name is the variable its answer sets.
// An Account input doesn't set one, and steps name it in an AccountReference. choices are for Choice and MultiChoice.
export interface InputDeclaration {
  name: string;
  label: string;
  help: string | null;
  kind: InputKind;
  choices: InputChoice[];
  default: string | null;
  required: boolean;
  maxLength: number | null;
  askAt: InputAsk;
  account: AccountDestination | null;
}

// version is the schema version. It goes up with each step kind or member that older agents would ignore. The server
// stores the lowest version a sequence needs, and leaves out variables and inputs while there are none.
export interface SequenceDefinition {
  version: number;
  steps: SequenceStep[];
  variables?: VariableDeclaration[] | null;
  inputs?: InputDeclaration[] | null;
}

// The path an IF took in a run.
export type IfBranch = "Then" | "Else";

// One test of a node as a run decided it, so a page can say why the run took a path. path is the test's field within
// the step, as a problem names it, such as "test.parts[1]". actual is the value it was tested against.
export interface TestEvaluation {
  path: string;
  held: boolean;
  actual: string | null;
}

// The phases a node of the tree may run in. There's more than one if it depends on the path an IF takes.
export interface NodePhase {
  nodeId: string;
  phases: SequencePhase[];
}

// A sequence with problems is kept as a draft and can't run. The facts let a dialog say what running it does without
// loading the whole document.
export interface SequenceSummary {
  id: string;
  name: string;
  description: string | null;
  revision: number;
  stepCount: number;
  problemCount: number;
  warningCount: number;
  erasesDisk: boolean;
  // The sequence joins the domain under the machine's name, or puts it in a cloud-init seed.
  needsComputerName: boolean;
  continuesInWindows: boolean;
  updatedUtc: string;
  updatedBy: string | null;
  // The raw disk image the sequence writes, if any, whether it starts with Secure Boot on, and which of Microsoft's
  // third-party UEFI CAs its boot file is signed under, in the same format as the machine's trustedUefiCas.
  rawImageName: string | null;
  rawImageBootCapability: ImageBootCapability | null;
  rawImageSignedUnder: string | null;
}

// SequenceDefinition.CurrentVersion: the highest version this page knows, and the one it writes. The server stores
// each sequence with the lowest version it needs.
export const SEQUENCE_VERSION = 3;

// Every problem keeps the sequence from running. Warnings don't.
export interface SequenceProblem {
  // Null for a problem of the whole sequence.
  stepId: string | null;
  // The camelCase name within the step, such as "script" or "conditions[1].value".
  field: string | null;
  // The server's English text. findingText shows code and args in the person's language.
  message: string;
  code?: string | null;
  args?: ServerArguments | null;
}

// Problems and warnings are worked out on every read, because a deleted image or a changed setting changes them.
export interface SequenceView {
  id: string;
  name: string;
  description: string | null;
  revision: number;
  definition: SequenceDefinition;
  // The phase each step runs in, in step order, as the engine decides it.
  stepPhases: SequencePhase[];
  problems: SequenceProblem[];
  warnings: SequenceProblem[];
  updatedUtc: string;
  updatedBy: string | null;
  // The phases of every node of the tree, in pre-order.
  nodePhases?: NodePhase[] | null;
}

// A starting point for a new sequence. name and description are the server's English text, next to their codes.
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

// The server's SequenceChangedEvent. revision is null if the sequence was deleted. sequence is the changed view, if
// the server sends it.
export interface SequenceChanged {
  id: string;
  revision: number | null;
  changedBy: string | null;
  sequence?: SequenceView | null;
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

// A separate root key, so a change to the list never refetches an open editor's document.
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
