// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { queryOptions, type QueryClient } from "@tanstack/react-query";

import type { AgentInput, InputAnswer } from "@/inputs/inputs";
import { apiDelete, apiGet, apiPost } from "@/lib/api";
import type { MachineSummary } from "@/machines/machines";
import type {
  IfBranch,
  SequenceDefinition,
  SequencePhase,
  StepState,
  TestEvaluation,
} from "@/sequences/sequences";
import type { ResolvedValue } from "@/values/values";

import { newerRun } from "./runCopies";

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

// A run of a task sequence. A deployment from before task sequences has no steps.
export interface DeploymentSummary {
  id: string;
  sequenceId: string | null;
  // The sequence's name when it was assigned, or the image's for a deployment from before task sequences.
  title: string;
  state: DeploymentState;
  source: DeploymentSource;
  requestedBy: string | null;
  stepCount: number;
  // From 0. With stepName, percent and phase, the step the agent reported last, which a failed run keeps.
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
  // The run needs someone, for answers to its inputs or to continue a pause. Servers before version 3 sequences leave
  // both out.
  waiting?: boolean;
  pauseMessage?: string | null;
}

// One step of a run as the agent last reported it; a run of a tree has one per node, containers included.
export interface DeploymentStepView {
  stepId: string;
  // The node's place in pre-order.
  index: number;
  name: string;
  // As the sequence document names the step's kind.
  kind: string;
  phase: SequencePhase;
  state: StepState;
  percent: number;
  // The server's times, taken when a report showed the step start and end.
  startedUtc: string | null;
  finishedUtc: string | null;
  error: string | null;
  // The container the node sits in, null at the top; depth counts containers from 0.
  parentId?: string | null;
  depth?: number;
  // The node's latest visit: the times it was entered, a repeat's time through its body, the path an IF took, and
  // its tests as they were decided.
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

// A run with the definition it was given, frozen when it was assigned, its steps and its files.
export interface DeploymentView {
  summary: DeploymentSummary;
  machineId: string;
  sequenceRevision: number | null;
  ruleId: string | null;
  // Null for a deployment from before task sequences.
  definition: SequenceDefinition | null;
  steps: DeploymentStepView[];
  artifacts: DeploymentArtifactView[];
  // Whoever started the run let it write a raw disk image that is not signed for Secure Boot.
  allowSecureBootMismatch: boolean;
  // Servers before version 3 sequences leave out the rest. values are as worked out when the run started, variables
  // as the agent last reported them, inputs the sequence's inputs and whether they're answered, pause where it waits.
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
