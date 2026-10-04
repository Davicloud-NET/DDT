// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { queryOptions } from "@tanstack/react-query";

import { apiGet } from "@/lib/api";

// One of the log files a Windows service writes into its store, newest first. A server that logs to its console, as
// in a container, lists none.
export interface LogFileView {
  name: string;
  size: number;
  writtenUtc: string;
}

export const logFilesQuery = queryOptions({
  queryKey: ["server-logs"],
  queryFn: () => apiGet<LogFileView[]>("/api/server/logs"),
});

export function logFileUrl(name: string): string {
  return `/api/server/logs/${encodeURIComponent(name)}`;
}
