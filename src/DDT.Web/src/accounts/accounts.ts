// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { queryOptions, type QueryClient } from "@tanstack/react-query";

import { apiErrorFrom, apiFetch, apiGet } from "@/lib/api";
import { reauthenticationToken, type SecretAction, type SecretState } from "@/settings/settings";

// The accounts steps use, Deployment > Accounts: a script runs as one, a share is connected with one, a join uses
// one. The password is stored encrypted and never sent to a page, which learns only whether it is set; it goes only to
// the step that uses it, while it runs. An account is bound to its destinations: domain, the domain a join with it may
// join, hosts, the share servers it may connect to, and runAs, whether a script may run as it.

// Where a step names the account, as a sequence's problem names the field: runAs, account or shares[0].account.
export interface AccountStepUse {
  stepId: string;
  stepName: string;
  field: string;
}

// A sequence that names the account, with its steps that do in the order of its tree.
export interface AccountUse {
  sequenceId: string;
  sequenceName: string;
  steps?: AccountStepUse[] | null;
}

export interface AccountView {
  id: string;
  name: string;
  userName: string;
  domain: string | null;
  hosts: string[];
  runAs: boolean;
  password: SecretState;
  usedBy: AccountUse[];
  revision: number;
  updatedUtc: string;
  updatedBy: string | null;
}

// revision is the one the page last read; a new account has none to name. password keeps, sets or clears it; a new
// user name or domain, or another server, needs it set again.
export interface SaveAccountRequest {
  revision: number;
  name: string;
  userName: string;
  domain: string | null;
  hosts: string[];
  runAs: boolean;
  password: SecretAction;
}

export const accountsQuery = queryOptions({
  queryKey: ["accounts"],
  queryFn: () => apiGet<AccountView[]>("/api/accounts"),
});

// Every write needs a person signed in on the web who entered their password again a few minutes ago; the server
// answers 403 with the code stepAccount.reauthenticate without it, and the page asks for the password.
async function send<T>(method: string, path: string, body?: unknown): Promise<T> {
  const token = reauthenticationToken();
  const response = await apiFetch(path, {
    method,
    headers: {
      ...(body === undefined ? {} : { "Content-Type": "application/json" }),
      ...(token === null ? {} : { "X-DDT-Reauthentication": token }),
    },
    ...(body === undefined ? {} : { body: JSON.stringify(body) }),
  });

  if (!response.ok) {
    throw await apiErrorFrom(response);
  }

  return response.status === 204 ? (undefined as T) : ((await response.json()) as T);
}

export function createAccount(request: SaveAccountRequest): Promise<AccountView> {
  return send<AccountView>("POST", "/api/accounts", request);
}

export function saveAccount(id: string, request: SaveAccountRequest): Promise<AccountView> {
  return send<AccountView>("PUT", `/api/accounts/${id}`, request);
}

// Refused with 409 while a sequence names the account.
export function deleteAccount(id: string): Promise<void> {
  return send<undefined>("DELETE", `/api/accounts/${id}`);
}

// The server's order: by name, ignoring case.
function byName(a: AccountView, b: AccountView): number {
  const x = a.name.toUpperCase();
  const y = b.name.toUpperCase();

  return x < y ? -1 : x > y ? 1 : 0;
}

export function putAccount(queryClient: QueryClient, account: AccountView): void {
  queryClient.setQueryData(accountsQuery.queryKey, (list) =>
    list === undefined
      ? list
      : [...list.filter((existing) => existing.id !== account.id), account].sort(byName),
  );
}

export function removeAccounts(queryClient: QueryClient, ids: readonly string[]): void {
  queryClient.setQueryData(accountsQuery.queryKey, (list) =>
    list?.filter((account) => !ids.includes(account.id)),
  );
}

// The server's AccountsRemovedEvent.
export interface AccountsRemoved {
  accountIds: string[];
}

// What an account's drawer edits, as typed. The password is only ever what is typed here, sent once and forgotten.
export interface AccountEdit {
  name: string;
  userName: string;
  domain: string;
  hosts: { key: string; host: string }[];
  runAs: boolean;
  password: SecretAction;
}

export function accountEditOf(account: AccountView | null): AccountEdit {
  return account === null
    ? {
        name: "",
        userName: "",
        domain: "",
        hosts: [],
        runAs: false,
        password: { action: "Set", value: "" },
      }
    : {
        name: account.name,
        userName: account.userName,
        domain: account.domain ?? "",
        hosts: account.hosts.map((host) => ({ key: crypto.randomUUID(), host })),
        runAs: account.runAs,
        password: { action: "Keep" },
      };
}

export function accountRequestOf(revision: number, edit: AccountEdit): SaveAccountRequest {
  const domain = edit.domain.trim();

  return {
    revision,
    name: edit.name.trim(),
    userName: edit.userName.trim(),
    domain: domain === "" ? null : domain,
    hosts: edit.hosts.map((row) => row.host.trim()),
    runAs: edit.runAs,
    // A new account left without a password has none.
    password:
      edit.password.action === "Set" && edit.password.value === ""
        ? { action: "Keep" }
        : edit.password,
  };
}

function sameText(a: string | null, b: string | null): boolean {
  return (a ?? "").trim().toLowerCase() === (b ?? "").trim().toLowerCase();
}

// Whether the edit sends a stored password somewhere it was not entered for: another user name or domain, or a server
// it did not name. The server refuses to keep it then, so the page asks for it beforehand.
export function reachesNewDestination(account: AccountView, edit: AccountEdit): boolean {
  return (
    !sameText(account.userName, edit.userName) ||
    !sameText(account.domain, edit.domain) ||
    edit.hosts.some(
      (row) => row.host.trim() !== "" && !account.hosts.some((host) => sameText(host, row.host)),
    )
  );
}
