// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { queryOptions } from "@tanstack/react-query";

import { apiErrorFrom, apiFetch, apiGet } from "@/lib/api";

import { reauthenticationToken } from "./settings";

// None: machines keep the agent of their boot image. Uploaded: on this page. Configuration: DDT:Agent:BinaryPath
// names the file, which the page then cannot replace.
export type AgentBinarySource = "None" | "Uploaded" | "Configuration";

// The agent netbooting machines switch to; the values are null when there is none.
export interface AgentBinaryView {
  sha256: string | null;
  size: number | null;
  uploadedUtc: string | null;
  uploadedBy: string | null;
  source: AgentBinarySource;
}

// The server takes an agent of at most this size.
export const maxAgentBytes = 128 * 1024 * 1024;

// No hub event announces an upload, so the page reads it again as other queries are, when it is older than a while.
export const agentBinaryQuery = queryOptions({
  queryKey: ["settings-agent"],
  queryFn: () => apiGet<AgentBinaryView>("/api/settings/agent"),
});

// The executable as the body, with the proof of identity it needs. The answer is the view as GET reads it from now on.
export async function uploadAgent(file: File): Promise<AgentBinaryView> {
  const token = reauthenticationToken();
  const response = await apiFetch("/api/settings/agent/binary", {
    method: "PUT",
    headers: {
      "Content-Type": "application/octet-stream",
      ...(token === null ? {} : { "X-DDT-Reauthentication": token }),
    },
    body: file,
  });

  if (!response.ok) {
    throw await apiErrorFrom(response);
  }

  return (await response.json()) as AgentBinaryView;
}
