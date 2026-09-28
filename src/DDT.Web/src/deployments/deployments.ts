// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { queryOptions } from "@tanstack/react-query";

import { apiDelete, apiGet, apiPost } from "@/lib/api";
import { serverText, type ServerArguments } from "@/lib/serverText";
import type { MachineSummary } from "@/machines/machines";
import type {
  IfBranch,
  SequenceDefinition,
  SequencePhase,
  StepState,
  TestEvaluation,
} from "@/sequences/sequences";

export type DeploymentState = "Assigned" | "Running" | "Done" | "Failed" | "Cancelled";

// Rule: an assignment rule chose the sequence and an operator approved the machine with it on the web.
export type DeploymentSource = "Web" | "Console" | "Rule";

// What the agent does between steps, so the page can say why no step is running.
export type RunActivity =
  | "Preparing"
  | "Step"
  | "HandingOver"
  | "Restarting"
  | "WaitingForWindowsSetup"
  | "Finishing"
  | "Removing";

// A run of a task sequence. Title is the sequence's name when it was assigned, or the image's for a deployment
// from before task sequences, which has no steps. stepIndex counts from 0; it, stepName, percent and phase are
// the step the agent reported last, which a failed run keeps. updatedUtc is the last change.
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

// A run with the definition it was given, frozen when it was assigned, its steps and its files. definition is
// null for a deployment from before task sequences.
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
}

export interface AssignSequenceRequest {
  sequenceId: string;
  computerName: string | null;
  // Lets the run write a raw disk image that is not signed for Secure Boot on a machine with Secure Boot on.
  allowSecureBootMismatch?: boolean;
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

// A step only moves forward, so a push that arrives after a newer read is ignored.
export function withStep(view: DeploymentView, step: DeploymentStepView): DeploymentView {
  const known = view.steps.find((candidate) => candidate.stepId === step.stepId);

  if (known === undefined || stepOrder[step.state] < stepOrder[known.state]) {
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
