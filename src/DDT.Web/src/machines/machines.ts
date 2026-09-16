import { queryOptions, type QueryClient } from "@tanstack/react-query";

import { apiGet, apiPost } from "@/lib/api";

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

export function formatMac(mac: string): string {
  return mac.match(/.{2}/g)?.join(":") ?? mac;
}
