// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { queryOptions, type QueryClient } from "@tanstack/react-query";

import { apiDelete, apiGet, apiPost } from "@/lib/api";

export type HardwareHashUploadState = "None" | "Pending" | "Imported" | "Failed";

// The server's RecoveryKeyView. protectorId is the key ID BitLocker's recovery screen shows.
export interface RecoveryKeyView {
  id: string;
  protectorId: string;
  volume: string;
  runId: string;
  createdUtc: string;
  replacedUtc: string | null;
}

export interface MachineHardwareHashView {
  serialNumber: string;
  groupTag: string | null;
  collectedUtc: string;
  uploadState: HardwareHashUploadState;
  uploadMessage: string | null;
}

// The server's MachineRecordsView, also pushed as machineRecordsChanged. It never holds a password.
export interface MachineRecordsView {
  machineId: string;
  recoveryKeys: RecoveryKeyView[];
  hardwareHash: MachineHardwareHashView | null;
}

export function machineRecordsKey(machineId: string) {
  return ["machines", machineId, "records"] as const;
}

export function machineRecordsQuery(machineId: string) {
  return queryOptions({
    queryKey: machineRecordsKey(machineId),
    queryFn: () => apiGet<MachineRecordsView>(`/api/machines/${machineId}/records`),
  });
}

// Only a page that already shows the machine's records takes the push.
export function putMachineRecords(queryClient: QueryClient, records: MachineRecordsView): void {
  if (queryClient.getQueryData(machineRecordsKey(records.machineId)) !== undefined) {
    queryClient.setQueryData(machineRecordsKey(records.machineId), records);
  }
}

export function dropRecoveryKey(queryClient: QueryClient, machineId: string, keyId: string): void {
  queryClient.setQueryData<MachineRecordsView>(machineRecordsKey(machineId), (records) =>
    records === undefined
      ? undefined
      : { ...records, recoveryKeys: records.recoveryKeys.filter((key) => key.id !== keyId) },
  );
}

export async function revealRecoveryKey(machineId: string, keyId: string): Promise<string> {
  const revealed = await apiPost<{ password: string }>(
    `/api/machines/${machineId}/recovery-keys/${keyId}/reveal`,
  );

  return revealed.password;
}

export function deleteRecoveryKey(machineId: string, keyId: string): Promise<void> {
  return apiDelete(`/api/machines/${machineId}/recovery-keys/${keyId}`);
}

// A key ID as typed: the whole GUID with or without braces and dashes, or its first 8 or more hex digits. Null for
// anything else, which the search then doesn't send.
export function keyIdDigits(text: string): string | null {
  const digits = text
    .trim()
    .replace(/^\{|\}$/g, "")
    .replaceAll("-", "")
    .toLowerCase();

  return /^[0-9a-f]{8,32}$/.test(digits) ? digits : null;
}

export function recoveryKeyMachines(digits: string): Promise<string[]> {
  return apiGet<string[]>(`/api/machines/recovery-keys?keyId=${digits}`);
}
