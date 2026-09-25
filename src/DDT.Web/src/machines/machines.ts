// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { queryOptions, type QueryClient } from "@tanstack/react-query";

import { isActive, type DeploymentSummary } from "@/deployments/deployments";
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
  // Whether the firmware started the agent with Secure Boot on; null when it did not say.
  secureBootEnabled: boolean | null;
}

// A hardware model as the machine's firmware reports it, compared without regard to case or runs of spaces. A
// null manufacturer matches any, and a model that ends in * matches every model that starts with the text
// before it.
export interface HardwareModel {
  manufacturer: string | null;
  model: string;
}

// A model the registered machines report, with how many report it, for the pickers of rules and package
// targets. Placeholders that firmware leaves in unset fields are left out.
export interface HardwareModelCount {
  manufacturer: string | null;
  model: string;
  machines: number;
}

export const modelsQuery = queryOptions({
  queryKey: ["machine-models"],
  queryFn: () => apiGet<HardwareModelCount[]>("/api/machines/models"),
});

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

// With the sequence the page showed a rule choosing, the approval also runs it, and the server refuses when the
// rules choose otherwise by now. Without one the approval runs nothing. allowSecureBootMismatch lets that run write a
// raw disk image that is not signed for Secure Boot.
export function approveMachine(
  id: string,
  expectedSequenceId: string | null = null,
  allowSecureBootMismatch = false,
): Promise<MachineSummary> {
  return apiPost<MachineSummary>(
    `/api/machines/${id}/approve`,
    expectedSequenceId === null
      ? undefined
      : allowSecureBootMismatch
        ? { expectedSequenceId, allowSecureBootMismatch }
        : { expectedSequenceId },
  );
}

export function rejectMachine(id: string): Promise<MachineSummary> {
  return apiPost<MachineSummary>(`/api/machines/${id}/reject`);
}

// Anyone who reaches the server can register a machine, so a waiting machine nobody ever approved may be a
// stray, and an operator can throw it away. One with an assigned sequence waits for it on purpose.
export function isStray(machine: MachineSummary): boolean {
  return machine.state === "Pending" && !machine.everApproved && !isActive(machine.deployment);
}

// A rejected machine stays rejected however often it registers. Removing it is the way back: it registers as a
// new machine at its next netboot.
export function isRemovable(machine: MachineSummary): boolean {
  return isStray(machine) || machine.state === "Rejected";
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

// The server's DeploymentLimits.WaitingAtPrompt, which decides whether an assignment authorizes a waiting
// machine; change both together. A machine waiting at the prompt checks in every few seconds and is
// recorded at most every 30 s.
export const WAITING_WINDOW_MS = 90_000;
