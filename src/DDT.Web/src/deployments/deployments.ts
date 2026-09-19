import { queryOptions } from "@tanstack/react-query";

import { apiDelete, apiGet, apiPost } from "@/lib/api";
import type { MachineSummary } from "@/machines/machines";

export type DeploymentState = "Assigned" | "Running" | "Done" | "Failed" | "Cancelled";

export type DeploymentStep = "Partition" | "Download" | "Apply" | "Boot" | "Unattend" | "Reboot";

export type DeploymentSource = "Web" | "Console";

export interface DeploymentSummary {
  id: string;
  imageId: string | null;
  imageName: string;
  state: DeploymentState;
  step: DeploymentStep | null;
  percent: number;
  source: DeploymentSource;
  requestedBy: string | null;
  createdUtc: string;
  startedUtc: string | null;
  finishedUtc: string | null;
  error: string | null;
}

export interface AssignImageRequest {
  imageId: string;
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

export function assignImage(
  machineId: string,
  request: AssignImageRequest,
): Promise<MachineSummary> {
  return apiPost<MachineSummary>(`/api/machines/${machineId}/deployments`, request);
}

// Cancels an Assigned deployment, or stops a Running one.
export function endDeployment(machineId: string): Promise<MachineSummary> {
  return apiDelete<MachineSummary>(`/api/machines/${machineId}/deployments/current`);
}
