// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { queryOptions, type QueryClient } from "@tanstack/react-query";

import { ApiError, apiErrorFrom, apiFetch, apiGet } from "@/lib/api";
import { reauthenticationToken, type SecretAction, type SecretState } from "@/settings/settings";

// A step that names the account. field is named like in a sequence's problems: runAs, account or shares[0].account.
export interface AccountStepUse {
  stepId: string;
  stepName: string;
  field: string;
}

// A sequence that names the account, with the steps that name it, in tree order.
export interface AccountUse {
  sequenceId: string;
  sequenceName: string;
  steps?: AccountStepUse[] | null;
}

// An account that steps use, bound to where it may be used. domain is the domain a join with it may join. hosts are
// the share servers it may connect to. runAs says whether a script may run as it.
export interface AccountView {
  id: string;
  name: string;
  userName: string;
  domain: string | null;
  hosts: string[];
  runAs: boolean;
  // Only whether one is set. The password only goes to the step that uses it, never to a page.
  password: SecretState;
  usedBy: AccountUse[];
  revision: number;
  updatedUtc: string;
  updatedBy: string | null;
}

// revision is the one the page last read. A new account doesn't have one yet. password keeps, sets or clears the
// password. A new user name, domain or server needs the password set again.
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

// Every write needs a person signed in on the web who entered their password again a few minutes ago. Otherwise the
// server answers 403 with the code stepAccount.reauthenticate, and the page asks for the password.
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

// Whether the server refused because it wants the person's password again. A 403 for an API token is final, because
// only someone signed in on the web may change accounts.
export function wantsReauthentication(error: unknown): boolean {
  return (
    error instanceof ApiError &&
    error.status === 403 &&
    error.problem?.code !== "stepAccount.apiToken"
  );
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

// The fields an account's drawer edits, as typed. The password is only what's typed here. It's sent once and then
// forgotten.
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
    // A new account whose password field is left empty is saved without one.
    password:
      edit.password.action === "Set" && edit.password.value === ""
        ? { action: "Keep" }
        : edit.password,
  };
}

function sameText(a: string | null, b: string | null): boolean {
  return (a ?? "").trim().toLowerCase() === (b ?? "").trim().toLowerCase();
}

// Whether the edit would send a stored password somewhere it wasn't entered for: another user name or domain, or a
// new server. The server won't keep the password then, so the page asks for it up front.
export function reachesNewDestination(account: AccountView, edit: AccountEdit): boolean {
  return (
    !sameText(account.userName, edit.userName) ||
    !sameText(account.domain, edit.domain) ||
    edit.hosts.some(
      (row) => row.host.trim() !== "" && !account.hosts.some((host) => sameText(host, row.host)),
    )
  );
}
