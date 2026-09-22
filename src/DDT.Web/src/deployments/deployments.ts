// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { queryOptions } from "@tanstack/react-query";

import { apiDelete, apiGet, apiPost } from "@/lib/api";
import type { MachineSummary } from "@/machines/machines";
import type { SequencePhase } from "@/sequences/sequences";

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

export interface AssignSequenceRequest {
  sequenceId: string;
  computerName: string | null;
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

// "Step 4 of 9: Apply image", or null before the agent reported a step.
export function currentStepLabel(deployment: DeploymentSummary): string | null {
  if (deployment.stepIndex === null) {
    return null;
  }

  const position = `step ${String(deployment.stepIndex + 1)} of ${String(deployment.stepCount)}`;

  return deployment.stepName === null ? position : `${position}: ${deployment.stepName}`;
}

// Null while a step runs, which the step itself describes.
export function activityLabel(activity: RunActivity | null): string | null {
  switch (activity) {
    case "Preparing":
      return "Preparing";
    case "HandingOver":
      return "Handing over to Windows";
    case "Restarting":
      return "Restarting";
    case "WaitingForWindowsSetup":
      return "Waiting for Windows setup";
    case "Finishing":
      return "Finishing";
    case "Removing":
      return "Removing the agent from Windows";
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
