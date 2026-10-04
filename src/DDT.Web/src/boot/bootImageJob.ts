// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { queryOptions, type QueryClient } from "@tanstack/react-query";

import { apiErrorFrom, apiFetch } from "@/lib/api";

// A build of the boot image or an install of the ADK, which the DDT Helper service runs for the server, or a build
// that a builder on another PC uploads. lines counts all its output so far.
export interface BootImageJob {
  kind: "Build" | "InstallAdk" | "Upload";
  state: "Running" | "Succeeded" | "Failed";
  startedUtc: string;
  finishedUtc: string | null;
  startedBy: string;
  problem: string | null;
  lines: number;
}

export interface BootImageJobLog {
  job: BootImageJob;
  lines: string[];
}

// A batch of output as the hub pushes it. first is the index of its first line in the job's output.
export interface BootImageJobOutput {
  startedUtc: string;
  first: number;
  lines: string[];
}

// Null when no job ran since the server started, which the server answers with 204.
export const bootImageJobQuery = queryOptions({
  queryKey: ["boot-image", "job"],
  queryFn: async (): Promise<BootImageJobLog | null> => {
    const response = await apiFetch("/api/boot-image/job");

    if (!response.ok) {
      throw await apiErrorFrom(response);
    }

    return response.status === 204 ? null : ((await response.json()) as BootImageJobLog);
  },
});

function readAgain(queryClient: QueryClient): void {
  void queryClient.invalidateQueries({ queryKey: bootImageJobQuery.queryKey, exact: true });
}

// Appends a batch that continues the output this page holds. Any other one means lines were missed, or the job is
// another, so the output is read again.
export function appendJobOutput(queryClient: QueryClient, output: BootImageJobOutput): void {
  const held = queryClient.getQueryData(bootImageJobQuery.queryKey);

  if (held === undefined) {
    return;
  }

  if (held?.job.startedUtc !== output.startedUtc || held.lines.length !== output.first) {
    readAgain(queryClient);

    return;
  }

  const lines = [...held.lines, ...output.lines];
  const next: BootImageJobLog = { job: { ...held.job, lines: lines.length }, lines };

  queryClient.setQueryData(bootImageJobQuery.queryKey, next);
}

// The view carries the job's state. Its output stays when the job is the one held, and is read again for a new one.
export function putJob(queryClient: QueryClient, job: BootImageJob | null): void {
  const held = queryClient.getQueryData(bootImageJobQuery.queryKey);

  if (held === undefined || job === null) {
    return;
  }

  if (held?.job.startedUtc !== job.startedUtc) {
    readAgain(queryClient);

    return;
  }

  const next: BootImageJobLog = { job, lines: held.lines };

  queryClient.setQueryData(bootImageJobQuery.queryKey, next);

  // A job that ended sends no more output, so lines this page missed would never arrive
  if (held.job.state === "Running" && job.state !== "Running" && job.lines > held.lines.length) {
    readAgain(queryClient);
  }
}
