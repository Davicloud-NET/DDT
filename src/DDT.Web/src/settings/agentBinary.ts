// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { queryOptions } from "@tanstack/react-query";

import { apiErrorFrom, apiFetch, apiGet } from "@/lib/api";

import { reauthenticationToken } from "./settings";

// None: machines keep the agent or console of their boot image. Uploaded: uploaded on this page. Bundled: the one the
// server came with, offered while nothing is uploaded. Configuration: DDT:Agent:BinaryPath or DDT:Agent:ConsolePath
// names the file, and the page can't replace it.
export type AgentBinarySource = "None" | "Uploaded" | "Bundled" | "Configuration";

// The agent that netbooting machines switch to. The values are null when there's none. version is the file's version
// where it has one. newerBundledVersion is set while an upload is offered and the server came with a newer one.
export interface AgentBinaryView {
  sha256: string | null;
  size: number | null;
  uploadedUtc: string | null;
  uploadedBy: string | null;
  source: AgentBinarySource;
  version: string | null;
  newerBundledVersion: string | null;
}

// The server takes an agent of at most this size, and a console of at most this size zipped and unpacked.
export const maxAgentBytes = 128 * 1024 * 1024;
export const maxConsoleBytes = maxAgentBytes;

// An upload answers with the view, and the hub's agentChanged brings it to other administrators' pages.
export const agentBinaryQuery = queryOptions({
  queryKey: ["settings-agent"],
  queryFn: () => apiGet<AgentBinaryView>("/api/settings/agent"),
});

// The console those machines show. The view describes its ddt-console.exe and the size of all its files. The hub's
// consoleChanged brings an upload to other administrators' pages.
export const consoleBinaryQuery = queryOptions({
  queryKey: ["settings-agent-console"],
  queryFn: () => apiGet<AgentBinaryView>("/api/settings/agent/console"),
});

// Sends the executable as the body, with the proof of identity it needs. The answer is the new view, the same one GET
// returns from now on.
export function uploadAgent(file: File): Promise<AgentBinaryView> {
  return upload("/api/settings/agent/binary", file, "application/octet-stream");
}

// Sends the zip of the folder Publish-Console.ps1 writes, the same way.
export function uploadConsole(file: File): Promise<AgentBinaryView> {
  return upload("/api/settings/agent/console", file, "application/zip");
}

// Takes machines back to the agent the server came with, or their boot image's. The answer is the new view.
export function removeAgentUpload(): Promise<AgentBinaryView> {
  return removeUpload("/api/settings/agent/binary");
}

export function removeConsoleUpload(): Promise<AgentBinaryView> {
  return removeUpload("/api/settings/agent/console");
}

async function removeUpload(path: string): Promise<AgentBinaryView> {
  const response = await apiFetch(path, { method: "DELETE" });

  if (!response.ok) {
    throw await apiErrorFrom(response);
  }

  return (await response.json()) as AgentBinaryView;
}

async function upload(path: string, file: File, contentType: string): Promise<AgentBinaryView> {
  const token = reauthenticationToken();
  const response = await apiFetch(path, {
    method: "PUT",
    headers: {
      "Content-Type": contentType,
      ...(token === null ? {} : { "X-DDT-Reauthentication": token }),
    },
    body: file,
  });

  if (!response.ok) {
    throw await apiErrorFrom(response);
  }

  return (await response.json()) as AgentBinaryView;
}
