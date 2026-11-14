// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { QueryClient } from "@tanstack/react-query";

import { apiErrorFrom, apiFetch, apiPost } from "@/lib/api";

import { machineRecordsKey, type MachineRecordsView } from "./machineRecords";

// The name Intune's import expects nothing of; the server sends the same.
export const autopilotCsvName = "autopilot.csv";

// Intune's CSV of the machines' hardware hashes. The server leaves out machines without one, and refuses when none
// has one.
export async function downloadAutopilotCsv(machineIds: readonly string[]): Promise<Blob> {
  const [only] = machineIds;
  const response =
    machineIds.length === 1 && only !== undefined
      ? await apiFetch(`/api/machines/${only}/autopilot.csv`)
      : await apiFetch("/api/machines/autopilot.csv", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ machineIds }),
        });

  if (!response.ok) {
    throw await apiErrorFrom(response);
  }

  return response.blob();
}

export function requestAutopilotUpload(machineId: string): Promise<void> {
  return apiPost(`/api/machines/${machineId}/autopilot/upload`);
}

// What the server now holds after the request; its push says the same.
export function markUploadPending(queryClient: QueryClient, machineId: string): void {
  queryClient.setQueryData<MachineRecordsView>(machineRecordsKey(machineId), (records) =>
    records?.hardwareHash === null || records?.hardwareHash === undefined
      ? records
      : {
          ...records,
          hardwareHash: { ...records.hardwareHash, uploadState: "Pending", uploadMessage: null },
        },
  );
}
