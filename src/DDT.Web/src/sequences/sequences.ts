// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { queryOptions } from "@tanstack/react-query";

import { apiDelete, apiGet, apiPost, apiPut } from "@/lib/api";
import type { ServerArguments } from "@/lib/serverText";
import type { ImageBootCapability } from "@/images/images";

export type SequencePhase = "WindowsPE" | "Windows";

// The server's ConditionOperator. Versions 1 and 2 know the first four; any other makes a document version 3, as an
// older agent's evaluator treats an operator it does not know as false.
export type ConditionOperator =
  | "Equals"
  | "NotEquals"
  | "StartsWith"
  | "Contains"
  | "NotContains"
  | "EndsWith"
  // * stands for any text and ? for one character.
  | "Matches"
  // value is a list separated by semicolons.
  | "In"
  // Whether the machine has a value at all; value is not read.
  | "Exists"
  | "NotExists"
  // Compared as numbers.
  | "Greater"
  | "GreaterOrEqual"
  | "Less"
  | "LessOrEqual"
  // An IPv4 address within a network written as 10.0.0.0/24.
  | "InSubnet";

export type ScriptInterpreter = "Cmd" | "PowerShell";

export type StepState = "Pending" | "Running" | "Done" | "Skipped" | "Failed";

// What kind of value a fact holds, which decides the operators that fit it. A YesNo value is "true" or "false".
export type FactType = "Text" | "Number" | "YesNo" | "IPv4" | "Mac";

// One name of the server's MachineVariableNames.Catalogue, as GET /api/sequences/facts lists them.
// changesDuringRun: the value can change while the run goes on, so a share's host cannot be made of it.
export interface FactView {
  name: string;
  type: FactType;
  changesDuringRun: boolean;
}

// The conditions of versions 1 and 2, kept beside when. variable is one of the server's MachineVariableNames, such
// as "Model", "MacAddress" or "Phase".
export interface StepCondition {
  variable: string;
  operator: ConditionOperator;
  value: string;
}

// A condition as a tree: groups whose parts must all, any or none hold, and tests at the leaves. A step's when, an
// IF's test and a repeat's until are one. An empty all or none holds, an empty any does not. variable names a fact,
// a run variable, or a value the sequence declares or rules and machine roles set.
export interface TestCondition {
  kind: "test";
  variable: string;
  operator: ConditionOperator;
  value: string;
}

export type ConditionGroupKind = "all" | "any" | "none";

export interface ConditionGroup {
  kind: ConditionGroupKind;
  parts: ConditionNode[];
}

export type ConditionNode = ConditionGroup | TestCondition;

// The account a step uses: exactly one of a stored account and an Account input the sequence declares, whose answer
// is kept for the one run. Never a password.
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

// id stays the same across edits: run state, reports and problems name a step by it. The step runs only when
// every condition holds and when holds as well. The members of version 3 are left out of the JSON while unset.
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

// The server picks the driver packages by the machine's model. requireMatch fails the step when it has none.
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

// Null takes the configured default; from version 3 organizationalUnit is a template. account, version 3, is the
// account that joins, which also names the domain; none takes the configured join account.
export interface JoinDomainStep extends StepBase {
  kind: "joinDomain";
  organizationalUnit: string | null;
  account?: AccountReference | null;
}

// packageId names a Files package that is extracted and becomes the script's working directory. runAs, version 3
// and the Windows phase only: the script runs as this account instead of SYSTEM, and never sees its password.
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

// Runs its steps in order. Its conditions and continueOnError apply to all of them. It connects no shares itself: a
// share belongs to the step inside that needs it, for as long as that step runs.
export interface GroupStep extends StepBase {
  kind: "group";
  steps: SequenceStep[];
}

// Runs then when test holds and else when it does not; both paths meet again after it. An else-if is an IF in else.
export interface IfStep extends StepBase {
  kind: "if";
  test: ConditionNode;
  then: SequenceStep[];
  else: SequenceStep[];
}

// Runs steps, then tests until, and again until it holds, at most maxTimes times (1 to 100). When until still does
// not hold after the last time, the repeat fails, unless goOnAtLimit lets the run go on.
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

// Waits until someone continues the run, or continueAfterMinutes (1 to 1440) have passed; null waits for as long as
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
// may be changed by a Set variable step or a script's output while the run goes on.
export interface VariableDeclaration {
  name: string;
  default: string | null;
  description: string | null;
  setBySteps: boolean;
}

export type InputKind = "Text" | "Choice" | "MultiChoice" | "YesNo" | "Account";

export type InputAsk = "Both" | "Web" | "Machine";

// label is what the person sees, value what the answer sets; a null label shows the value.
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

// Something asked before the run starts, on the web, at the machine or both. name is the variable its answer sets;
// an Account input sets none, and steps name it in an AccountReference. choices are for Choice and MultiChoice.
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

// version is the document schema, raised when a step kind or a member older agents would ignore is added. The
// server stores each sequence with the lowest version it needs, and leaves variables and inputs out while there are
// none.
export interface SequenceDefinition {
  version: number;
  steps: SequenceStep[];
  variables?: VariableDeclaration[] | null;
  inputs?: InputDeclaration[] | null;
}

// The path an IF took in a run.
export type IfBranch = "Then" | "Else";

// One test of a node as a run decided it, so a page can say why it took a path. path is the test's field within
// the step, as a problem names it, such as "test.parts[1]"; actual is the value it was tested against.
export interface TestEvaluation {
  path: string;
  held: boolean;
  actual: string | null;
}

// The phases a node of the tree may run in: more than one where it depends on the path an IF takes.
export interface NodePhase {
  nodeId: string;
  phases: SequencePhase[];
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

// SequenceDefinition.CurrentVersion: the highest version this page knows, which it writes. The server stores each
// sequence with the lowest version it needs.
export const SEQUENCE_VERSION = 3;

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
// are worked out on every read, because a deleted image or a changed setting changes them. nodePhases holds the
// phases of every node of the tree, in pre-order.
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
  nodePhases?: NodePhase[] | null;
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

// The server's SequenceChangedEvent. revision is null when the sequence was deleted; sequence is the view that
// changed, from servers that send it.
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
