// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { queryOptions } from "@tanstack/react-query";

import { apiErrorFrom, apiFetch, apiGet } from "@/lib/api";

import { reauthenticationToken } from "../../settings";

// A process that holds one of the netboot ports. service names it where DDT knows what it is.
export interface NetbootPortOwner {
  processId: number;
  process: string;
  service: "DDT" | "DHCP" | "WDS" | null;
}

export interface NetbootPort {
  port: number;
  owners: NetbootPortOwner[];
}

export interface NetbootService {
  installed: boolean;
  running: boolean;
}

// What else answers netboot on the server, and what a DHCP server has to say to send machines to DDT. ports is null on
// a server that is not Windows. helper says whether DDT can change the DHCP server and WDS on this computer itself.
export interface NetbootNeighbours {
  ports: NetbootPort[] | null;
  dhcp: NetbootService;
  wds: NetbootService;
  helper: boolean;
  bootServer: string;
  bootFile: string;
  // DDT leaves UDP 67 to the DHCP server of the server's computer and answers on 4011 alone.
  leavesDhcpPort: boolean;
  // Whether that DHCP server sends option 60, PXEClient, which sends machines to 4011. null where nobody could ask it.
  dhcpSendsPxe: boolean | null;
}

// A scope of the Microsoft DHCP server on the server's computer, with the options 66 and 67 it has now.
export interface DhcpScope {
  scopeId: string;
  name: string;
  active: boolean;
  bootServer: string | null;
  bootFile: string | null;
}

export const neighboursQuery = queryOptions({
  queryKey: ["netboot"],
  queryFn: () => apiGet<NetbootNeighbours>("/api/netboot"),
});

export const dhcpScopesQuery = queryOptions({
  queryKey: ["netboot", "dhcp-scopes"],
  queryFn: () => apiGet<DhcpScope[]>("/api/netboot/dhcp-scopes"),
  staleTime: 0,
});

// Each change decides what every machine that netboots loads, so it carries the proof of a password typed again.
async function change<T>(path: string, body?: unknown): Promise<T> {
  const token = reauthenticationToken();
  const response = await apiFetch(path, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      ...(token === null ? {} : { "X-DDT-Reauthentication": token }),
    },
    ...(body === undefined ? {} : { body: JSON.stringify(body) }),
  });

  if (!response.ok) {
    throw await apiErrorFrom(response);
  }

  return (await response.json()) as T;
}

// Answers with the scopes as they are afterwards.
export function setDhcpOptions(scopes: string[]): Promise<DhcpScope[]> {
  return change("/api/netboot/dhcp-options", { scopes });
}

// Option 60 on the DHCP server of the server's computer. Answers with the neighbours as they are afterwards.
export function setDhcpPxe(send: boolean): Promise<NetbootNeighbours> {
  return change("/api/netboot/dhcp-pxe", { send });
}

export type WdsChange = "replace" | "restore" | "boot-image";

export function changeWds(action: WdsChange): Promise<NetbootNeighbours> {
  return change(`/api/netboot/wds/${action}`);
}

// For a Microsoft DHCP server on another computer: run there, in an elevated PowerShell.
export function dhcpCommand(view: NetbootNeighbours): string {
  return `Set-DhcpServerv4OptionValue -OptionId 66 -Value "${view.bootServer}"; Set-DhcpServerv4OptionValue -OptionId 67 -Value "${view.bootFile}"`;
}
