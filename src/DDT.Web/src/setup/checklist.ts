// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { queryOptions, type QueryClient } from "@tanstack/react-query";

import { apiGet } from "@/lib/api";

// What a new server still needs before its first deployment. Each is true once the server sees it done.
export interface SetupChecklist {
  password: boolean;
  netboot: boolean;
  bootImage: boolean;
  image: boolean;
  sequence: boolean;
  machine: boolean;
  // The Linux boot environment can be started, which needs no build. Missing from a server that does not say.
  linuxBoot?: boolean;
}

export type ChecklistItem = keyof SetupChecklist;

// Machines can netboot once either boot environment is there: a built boot image, or the Linux one DDT comes with.
export function canNetboot(checklist: SetupChecklist): boolean {
  return checklist.bootImage || checklist.linuxBoot === true;
}

export const checklistQuery = queryOptions({
  queryKey: ["setup-checklist"],
  queryFn: () => apiGet<SetupChecklist>("/api/server/checklist"),
});

export function isComplete(checklist: SetupChecklist): boolean {
  return (
    canNetboot(checklist) &&
    checklist.password &&
    checklist.netboot &&
    checklist.image &&
    checklist.sequence &&
    checklist.machine
  );
}

// The hub's pushes say what was done, so an item ticks itself without the list being read again.
export function setChecklistItem(
  queryClient: QueryClient,
  item: ChecklistItem,
  done: boolean,
): void {
  queryClient.setQueryData(checklistQuery.queryKey, (checklist) =>
    checklist === undefined || checklist[item] === done
      ? checklist
      : { ...checklist, [item]: done },
  );
}

const DISMISSED_KEY = "ddt.checklist.dismissed";

// Dismissing hides the card on the first page in this browser. The Server page keeps it.
export function isDismissed(): boolean {
  try {
    return window.localStorage.getItem(DISMISSED_KEY) === "1";
  } catch {
    return false;
  }
}

export function dismiss(): void {
  try {
    window.localStorage.setItem(DISMISSED_KEY, "1");
  } catch {
    // A browser without storage shows the card again at its next load
  }
}
