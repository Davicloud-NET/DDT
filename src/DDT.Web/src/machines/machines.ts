import { queryOptions, type QueryClient } from "@tanstack/react-query";

import type { DeploymentSummary } from "@/deployments/deployments";
import { apiDelete, apiGet, apiPost } from "@/lib/api";

export type MachineState =
  "Pending" | "Approved" | "Deploying" | "Done" | "Failed" | "Rejected" | "Retired";

export interface MachineSummary {
  id: string;
  state: MachineState;
  smbiosUuid: string;
  primaryMac: string;
  macAddresses: string[];
  manufacturer: string | null;
  model: string | null;
  serialNumber: string | null;
  assignedName: string | null;
  agentVersion: string | null;
  firstSeenUtc: string;
  lastSeenUtc: string;
  lastSeenAddress: string | null;
  signedInBy: string | null;
  firstSeenAddress: string | null;
  everApproved: boolean;
  // The eligible disks as the agent reported them, for example "Disk 0: Msft Virtual Disk, 64 GB, SCSI".
  disks: string | null;
  // Null for an agent that does not report disks.
  eligibleDiskCount: number | null;
  // The active deployment, else the latest finished one.
  deployment: DeploymentSummary | null;
}

export const machinesQuery = queryOptions({
  queryKey: ["machines"],
  queryFn: () => apiGet<MachineSummary[]>("/api/machines"),
});

// The server's order. Last seen changes on every poll, so ordering by it would move rows under the pointer.
export function compareMachines(a: MachineSummary, b: MachineSummary): number {
  return (
    Number(b.state === "Pending") - Number(a.state === "Pending") ||
    Date.parse(b.firstSeenUtc) - Date.parse(a.firstSeenUtc) ||
    a.id.localeCompare(b.id)
  );
}

export function upsertMachine(queryClient: QueryClient, machine: MachineSummary): void {
  queryClient.setQueryData<MachineSummary[]>(machinesQuery.queryKey, (machines) => {
    if (machines === undefined) {
      return undefined;
    }

    const others = machines.filter((existing) => existing.id !== machine.id);

    return [machine, ...others].sort(compareMachines);
  });
}

export function approveMachine(id: string): Promise<MachineSummary> {
  return apiPost<MachineSummary>(`/api/machines/${id}/approve`);
}

export function rejectMachine(id: string): Promise<MachineSummary> {
  return apiPost<MachineSummary>(`/api/machines/${id}/reject`);
}

// Anyone who reaches the server can register a machine, so a waiting machine nobody ever approved may be a
// stray, and an operator can throw it away.
export function isStray(machine: MachineSummary): boolean {
  return machine.state === "Pending" && !machine.everApproved;
}

export function removeMachine(id: string): Promise<void> {
  return apiDelete(`/api/machines/${id}`);
}

export function removeWaitingFrom(address: string): Promise<void> {
  return apiDelete(`/api/machines?waitingFrom=${encodeURIComponent(address)}`);
}

export function formatMac(mac: string): string {
  return mac.match(/.{2}/g)?.join(":") ?? mac;
}

// How a sentence names the machine. Many machines share a model, so the MAC tells them apart.
export function machineLabel(machine: MachineSummary): string {
  if (machine.assignedName !== null) {
    return machine.assignedName;
  }

  const mac = formatMac(machine.primaryMac);

  return machine.model === null ? `the machine with MAC ${mac}` : `${machine.model} (${mac})`;
}

// A machine waiting at the prompt checks in every few seconds and is recorded at most every 30 s.
export const WAITING_WINDOW_MS = 90_000;
