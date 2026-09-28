// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { queryOptions, type QueryClient } from "@tanstack/react-query";

import type { AgentInput, InputAnswer } from "@/inputs/inputs";
import { ApiError, apiDelete, apiGet, apiPost } from "@/lib/api";
import { serverText, type ServerArguments } from "@/lib/serverText";
import type { MachineSummary } from "@/machines/machines";
import type {
  IfBranch,
  SequenceDefinition,
  SequencePhase,
  StepState,
  TestEvaluation,
} from "@/sequences/sequences";
import type { ResolvedValue } from "@/values/values";

export type DeploymentState = "Assigned" | "Running" | "Done" | "Failed" | "Cancelled";

// Rule: a rule chose the sequence and an operator approved the machine with it on the web.
export type DeploymentSource = "Web" | "Console" | "Rule";

// What the agent does between steps, so the page can say why no step is running. WaitingForInput: the run waits at
// its start for answers to its inputs. Paused: a Pause step waits for someone to continue the run.
export type RunActivity =
  | "Preparing"
  | "Step"
  | "HandingOver"
  | "Restarting"
  | "WaitingForWindowsSetup"
  | "Finishing"
  | "Removing"
  | "WaitingForInput"
  | "Paused";

// A run of a task sequence. Title is the sequence's name when it was assigned, or the image's for a deployment
// from before task sequences, which has no steps. stepIndex counts from 0; it, stepName, percent and phase are
// the step the agent reported last, which a failed run keeps. updatedUtc is the last change. waiting says the run needs
// someone, for answers to its inputs or to continue a pause, whose message is pauseMessage; servers before version 3
// sequences leave both out.
export interface DeploymentSummary {
  id: string;
  sequenceId: string | null;
  title: string;
  state: DeploymentState;
  source: DeploymentSource;
  requestedBy: string | null;
  stepCount: number;
  stepIndex: number | null;
  stepName: string | null;
  percent: number;
  phase: SequencePhase | null;
  activity: RunActivity | null;
  createdUtc: string;
  startedUtc: string | null;
  finishedUtc: string | null;
  updatedUtc: string;
  error: string | null;
  waiting?: boolean;
  pauseMessage?: string | null;
}

// One step of a run as the agent last reported it. kind is the step's kind as the sequence document names it.
// The times are the server's, taken when a report showed the step start and end.
//
// A run of a tree has a step per node, containers included, and index is the node's place in pre-order. parentId is
// the container it sits in, null at the top, and depth counts containers from 0. pass, iteration, branch and
// evaluation are the node's latest visit: pass counts the times it was entered, iteration is a repeat's time
// through its body, branch the path an IF took, and evaluation its tests as they were decided.
export interface DeploymentStepView {
  stepId: string;
  index: number;
  name: string;
  kind: string;
  phase: SequencePhase;
  state: StepState;
  percent: number;
  startedUtc: string | null;
  finishedUtc: string | null;
  error: string | null;
  parentId?: string | null;
  depth?: number;
  pass?: number;
  iteration?: number;
  branch?: IfBranch | null;
  evaluation?: TestEvaluation[] | null;
}

// What a run downloads: the image it applies, or a package of drivers or files.
export type ArtifactKind = "Image" | "Drivers" | "Files";

// A file the run downloads, frozen when the run was assigned. sourceId is the image or package it came from,
// which may have been deleted since.
export interface DeploymentArtifactView {
  stepId: string;
  kind: ArtifactKind;
  sourceId: string;
  name: string;
  sha256: string;
  sizeBytes: number;
}

// An input of a run and whether it has its answer; the answer itself is among the run's values, and an Account
// input's never leaves the server. answeredBy is who gave it, a user's name or the machine.
export interface RunInputView {
  input: AgentInput;
  answered: boolean;
  answeredBy: string | null;
}

// The Pause step a run waits at, and its visit, which continuing names. message is worked out by the agent;
// continuesUtc is when the pause goes on by itself, null when it waits for someone.
export interface RunPauseView {
  stepId: string;
  pass: number;
  message: string | null;
  sinceUtc: string | null;
  continuesUtc: string | null;
}

// A run with the definition it was given, frozen when it was assigned, its steps and its files. definition is
// null for a deployment from before task sequences.
//
// values are the run's values as they were worked out when it started, each with where it came from. variables are
// the sequence's variables as the agent last reported them. inputs are the sequence's inputs and whether they are
// answered, and pause the pause the run waits at. Servers before version 3 sequences leave them out, and each is null
// where the run has none.
export interface DeploymentView {
  summary: DeploymentSummary;
  machineId: string;
  sequenceRevision: number | null;
  ruleId: string | null;
  definition: SequenceDefinition | null;
  steps: DeploymentStepView[];
  artifacts: DeploymentArtifactView[];
  // Whoever started the run let it write a raw disk image that is not signed for Secure Boot.
  allowSecureBootMismatch: boolean;
  values?: ResolvedValue[] | null;
  variables?: Record<string, string> | null;
  inputs?: RunInputView[] | null;
  pause?: RunPauseView | null;
}

// answers are the answers to the sequence's inputs asked on the web; the server names a refused one's field
// "answers.<name>".
export interface AssignSequenceRequest {
  sequenceId: string;
  computerName: string | null;
  // Lets the run write a raw disk image that is not signed for Secure Boot on a machine with Secure Boot on.
  allowSecureBootMismatch?: boolean;
  answers?: InputAnswer[];
}

export interface DeploymentOptionsView {
  domainConfigured: boolean;
  // DDT:Machines:RequireWebApproval. On, a sign-in at a machine only records who is there.
  requireWebApproval: boolean;
  // True when DDT:Machines:ZeroTouchNetworks lists a network and web approval is off.
  zeroTouchEnabled: boolean;
  // The server's clock when it answered.
  serverUtc: string;
}

export interface DeploymentOptions extends DeploymentOptionsView {
  // Milliseconds to add to this browser's clock to get the server's, measured when the answer arrived.
  // The server decides with its own clock whether a machine is waiting at the prompt.
  serverClockOffsetMs: number;
}

// Assigned and Running deployments hold the machine: it cannot get another one until they end.
export function isActive(deployment: DeploymentSummary | null): boolean {
  return deployment?.state === "Assigned" || deployment?.state === "Running";
}

// "step 4 of 9: Apply image", to put in a sentence, or null before the agent reported a step.
export function currentStepLabel(deployment: DeploymentSummary): string | null {
  if (deployment.stepIndex === null) {
    return null;
  }

  const number = deployment.stepIndex + 1;
  const count = deployment.stepCount;
  const name = deployment.stepName;

  return name === null ? t`step ${number} of ${count}` : t`step ${number} of ${count}: ${name}`;
}

// Who put the run on the machine, for a run that has not started.
export function assignedBy(deployment: DeploymentSummary): string {
  const by = deployment.requestedBy;

  switch (deployment.source) {
    case "Web":
      return by === null ? t`Assigned` : t`Assigned by ${by}`;
    case "Rule":
      return by === null
        ? t`Approved with the sequence a rule chose`
        : t`Approved by ${by} with the sequence a rule chose`;
    case "Console":
      return by === null ? t`Chosen at the machine` : t`Chosen at the machine by ${by}`;
  }
}

// Null while a step runs, which the step itself describes.
export function activityLabel(activity: RunActivity | null): string | null {
  switch (activity) {
    case "Preparing":
      return t`Preparing`;
    case "HandingOver":
      return t`Handing over to Windows`;
    case "Restarting":
      return t`Restarting`;
    case "WaitingForWindowsSetup":
      return t`Waiting for Windows setup`;
    case "Finishing":
      return t`Finishing`;
    case "Removing":
      return t`Removing the agent from Windows`;
    case "WaitingForInput":
      return t`Waiting for answers`;
    case "Paused":
      return t`Paused`;
    case "Step":
    case null:
      return null;
  }
}

// While the machine restarts or Windows sets itself up, the agent does not report, so the last contact is what
// tells a slow setup from a machine that is gone.
export function isSilentActivity(activity: RunActivity | null): boolean {
  return (
    activity === "Restarting" || activity === "HandingOver" || activity === "WaitingForWindowsSetup"
  );
}

export type DomainJoinFindingLevel = "Passed" | "Warning" | "Problem";

// text is the server's English, which domainFindingText says in the person's language.
export interface DomainJoinFinding {
  level: DomainJoinFindingLevel;
  text: string;
  code?: string | null;
  args?: ServerArguments | null;
}

export function domainFindingText(finding: DomainJoinFinding): string {
  return serverText(finding.code, finding.args, finding.text);
}

// What the domain said about the join account, in the order it was asked. Container is the organizational unit or
// the default Computers container, null when the check stopped before it.
export interface DomainJoinCheckView {
  canJoin: boolean;
  domain: string | null;
  userName: string | null;
  controller: string | null;
  container: string | null;
  findings: DomainJoinFinding[];
  checkedUtc: string;
}

// Signs in to the domain as the join account, so only administrators may. Null takes the configured default unit.
export function checkDomainJoin(organizationalUnit: string | null): Promise<DomainJoinCheckView> {
  return apiPost<DomainJoinCheckView>("/api/deployments/domain-check", { organizationalUnit });
}

// The settings come from the server's configuration and change only with a restart. The clock offset is
// measured again with every answer.
export const deploymentOptionsQuery = queryOptions({
  queryKey: ["deployment-options"],
  queryFn: async (): Promise<DeploymentOptions> => {
    const view = await apiGet<DeploymentOptionsView>("/api/deployments/options");
    const serverNow = Date.parse(view.serverUtc);

    return {
      ...view,
      serverClockOffsetMs: Number.isNaN(serverNow) ? 0 : serverNow - Date.now(),
    };
  },
  staleTime: 5 * 60_000,
});

export function deploymentQuery(deploymentId: string) {
  return queryOptions({
    queryKey: ["deployment", deploymentId],
    queryFn: () => apiGet<DeploymentView>(`/api/deployments/${deploymentId}`),
  });
}

// Newest first.
export function machineDeploymentsQuery(machineId: string) {
  return queryOptions({
    queryKey: ["machine-deployments", machineId],
    queryFn: () => apiGet<DeploymentSummary[]>(`/api/machines/${machineId}/deployments`),
  });
}

const stepOrder: Record<StepState, number> = {
  Pending: 0,
  Running: 1,
  Done: 2,
  Skipped: 2,
  Failed: 2,
};

// Whether a push of a step is at least as new as the copy the page has. A node inside a repeat is entered again with a
// higher pass, which starts a new visit and replaces the last one; within a visit a step only moves forward, and a
// repeat only goes on to later times through its body. A push that arrives after a newer read is ignored.
export function isNewerStep(known: DeploymentStepView, step: DeploymentStepView): boolean {
  const knownPass = known.pass ?? 0;
  const pass = step.pass ?? 0;

  if (pass !== knownPass) {
    return pass > knownPass;
  }

  const order = stepOrder[step.state] - stepOrder[known.state];

  return order !== 0 ? order > 0 : (step.iteration ?? 0) >= (known.iteration ?? 0);
}

// The run with the pushed step in place of its older copy, keyed by the node and its pass.
export function withStep(view: DeploymentView, step: DeploymentStepView): DeploymentView {
  const known = view.steps.find((candidate) => candidate.stepId === step.stepId);

  if (known === undefined || !isNewerStep(known, step)) {
    return view;
  }

  return {
    ...view,
    steps: view.steps.map((candidate) => (candidate.stepId === step.stepId ? step : candidate)),
  };
}

// The later of two copies of one run, which the machine list and a read of the run can each hold.
export function newerRun(a: DeploymentSummary, b: DeploymentSummary): DeploymentSummary {
  return Date.parse(b.updatedUtc) > Date.parse(a.updatedUtc) ? b : a;
}

// The machine list follows the machine's current run live, so the history takes it from there.
export function withCurrentRun(
  history: readonly DeploymentSummary[],
  current: DeploymentSummary | null,
): DeploymentSummary[] {
  if (current === null) {
    return [...history];
  }

  return history.some((run) => run.id === current.id)
    ? history.map((run) => (run.id === current.id ? newerRun(run, current) : run))
    : [current, ...history];
}

export function assignSequence(
  machineId: string,
  request: AssignSequenceRequest,
): Promise<MachineSummary> {
  return apiPost<MachineSummary>(`/api/machines/${machineId}/deployments`, request);
}

// Cancels an Assigned deployment, or stops a Running one.
export function endDeployment(machineId: string): Promise<MachineSummary> {
  return apiDelete<MachineSummary>(`/api/machines/${machineId}/deployments/current`);
}

// The server's ContinueRunRequest: the Pause step and the visit the page showed, so a click that comes late continues
// no later pause.
export interface ContinueRunRequest {
  stepId: string;
  pass: number;
}

// The run as the server has it after an answer or a continue. late is set where the server refused, because the pause
// was continued already or the answers were given at the machine first; the run is then as it is now.
export interface RunAnswer {
  view: DeploymentView;
  late: boolean;
}

function isDeploymentView(body: unknown): body is DeploymentView {
  return typeof body === "object" && body !== null && "summary" in body && "steps" in body;
}

// A refusal with 409 carries the run as it is now.
async function withLateAnswer(send: () => Promise<DeploymentView>): Promise<RunAnswer> {
  try {
    return { view: await send(), late: false };
  } catch (error) {
    if (error instanceof ApiError && error.status === 409 && isDeploymentView(error.problem)) {
      return { view: error.problem, late: true };
    }

    throw error;
  }
}

// Lets a run go on that a Pause step holds. Operators only.
export function continueRun(machineId: string, request: ContinueRunRequest): Promise<RunAnswer> {
  return withLateAnswer(() =>
    apiPost<DeploymentView>(`/api/machines/${machineId}/deployments/current/continue`, request),
  );
}

// Answers the inputs a run waits for at its start. Operators only; a refused answer's field is "answers.<name>".
export function answerInputs(machineId: string, answers: InputAnswer[]): Promise<RunAnswer> {
  return withLateAnswer(() =>
    apiPost<DeploymentView>(`/api/machines/${machineId}/deployments/current/answers`, {
      answers,
    }),
  );
}

// Whether the run needs someone: answers to its inputs, or a pause to continue.
export function isWaiting(run: DeploymentSummary | null): boolean {
  return (
    run?.state === "Running" &&
    (run.waiting === true || run.activity === "Paused" || run.activity === "WaitingForInput")
  );
}

// Puts a run the server answered with into the cache: the run itself, and the machine's copy of its summary where the
// machine list has it. The hub pushes the same for everyone else looking.
export function putRun(queryClient: QueryClient, view: DeploymentView): void {
  queryClient.setQueryData(deploymentQuery(view.summary.id).queryKey, view);
  queryClient.setQueryData<MachineSummary[]>(["machines"], (machines) =>
    machines?.map((machine) =>
      machine.deployment?.id === view.summary.id
        ? { ...machine, deployment: newerRun(machine.deployment, view.summary) }
        : machine,
    ),
  );
}
