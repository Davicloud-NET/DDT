// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { queryOptions, type QueryClient } from "@tanstack/react-query";

import { apiErrorFrom, apiFetch, apiGet, apiPost, ApiError } from "@/lib/api";

// The settings the pages edit, as the settings API has them (docs/settings.md, sections 6 and "Built in M6.5"). Each
// section is one form and one unit of validation; the stored document is the desired state, applied live or by
// rebuilding a subsystem inside the running server.

export type SettingsSectionName =
  "deployment" | "machines" | "ldap" | "oidc" | "proxies" | "pxe" | "logging";

// A secret is never sent back: the page learns whether it is set, and whether the key ring can still read it.
export interface SecretState {
  isSet: boolean;
  unreadable: boolean;
  updatedUtc: string | null;
}

// A field whose key is present in configuration. Configuration wins, and the page shows the value read-only.
export interface SettingsLock {
  field: string;
  configurationKey: string;
  environmentVariable: string;
  source: string;
  storedDiffers: boolean;
}

// A problem keeps the section closed until it is fixed; a warning's code has to be confirmed by the save that raises
// it. field is the field's name on the page, such as domain.name, and empty for the whole section.
export interface SettingsFinding {
  field: string;
  message: string;
  code: string | null;
}

export type ApplyStateName = "Applied" | "Failed" | "Pending";

// How far each host has applied a section that rebuilds a subsystem, such as the network boot listeners.
export interface SettingsApplyState {
  host: string;
  version: number;
  state: ApplyStateName;
  message: string | null;
  updatedUtc: string | null;
}

export interface SettingsSectionView<T> {
  section: string;
  version: number;
  updatedUtc: string | null;
  updatedBy: string | null;
  values: T;
  secrets: Record<string, SecretState>;
  locked: SettingsLock[];
  problems: SettingsFinding[];
  warnings: SettingsFinding[];
  apply: SettingsApplyState[] | null;
  // The fields a save may change only with a fresh proof of identity.
  reauthenticate: string[];
}

export type SecretAction =
  { action: "Keep" } | { action: "Set"; value: string } | { action: "Clear" };

export interface SettingsSectionUpdate<T> {
  version: number;
  values: T;
  secrets: Record<string, SecretAction>;
  confirm: string[];
}

export function settingsKey(section: string) {
  return ["settings", section] as const;
}

export function settingsQuery<T>(section: SettingsSectionName) {
  return queryOptions({
    queryKey: settingsKey(section),
    queryFn: () => apiGet<SettingsSectionView<T>>(`/api/settings/${section}`),
  });
}

// The token that proves the password was typed again recently, for fields that grant roles or trust. It lives only
// in memory, for the few minutes the server accepts it.
let reauthentication: { token: string; expires: number } | null = null;

export function reauthenticationToken(): string | null {
  return reauthentication !== null && reauthentication.expires > Date.now()
    ? reauthentication.token
    : null;
}

export async function reauthenticate(password: string, code: string | null): Promise<void> {
  const answer = await apiPost<{ token: string; expiresUtc: string }>(
    "/api/settings/reauthenticate",
    { password, code },
  );

  // A little before the server's end, so a save does not go out with a token that expires on the way.
  reauthentication = { token: answer.token, expires: Date.parse(answer.expiresUtc) - 10_000 };
}

// What a refused save means for the form.
export type SaveRefusal =
  // Someone saved the section since it was loaded.
  | { kind: "stale"; message: string }
  // Fields that need a fresh proof of identity were changed without one.
  | { kind: "reauthenticate"; message: string; fields: string[] }
  // Field problems, and warnings the save has to confirm.
  | { kind: "invalid"; fields: Record<string, string[]>; confirm: SettingsFinding[] }
  | { kind: "other"; message: string };

interface SettingsProblem {
  errors?: Record<string, string[]>;
  confirm?: SettingsFinding[];
  fields?: string[];
}

export function refusalOf(error: unknown): SaveRefusal {
  if (!(error instanceof ApiError)) {
    return { kind: "other", message: error instanceof Error ? error.message : String(error) };
  }

  const problem = (error.problem ?? {}) as SettingsProblem;

  if (error.status === 409) {
    return { kind: "stale", message: error.message };
  }

  if (error.status === 403) {
    return { kind: "reauthenticate", message: error.message, fields: problem.fields ?? [] };
  }

  if (error.status === 400) {
    const { confirm: unconfirmed = [], ...fields } = problem.errors ?? {};

    return {
      kind: "invalid",
      fields,
      confirm:
        problem.confirm ??
        unconfirmed.map((message) => {
          const colon = message.indexOf(": ");

          return colon < 0
            ? { field: "", message, code: null }
            : { field: "", message: message.slice(colon + 2), code: message.slice(0, colon) };
        }),
    };
  }

  return { kind: "other", message: error.message };
}

export async function saveSettings<T>(
  section: SettingsSectionName,
  update: SettingsSectionUpdate<T>,
  headers: Record<string, string> = {},
): Promise<SettingsSectionView<T>> {
  const token = reauthenticationToken();
  const response = await apiFetch(`/api/settings/${section}`, {
    method: "PUT",
    headers: {
      "Content-Type": "application/json",
      ...(token === null ? {} : { "X-DDT-Reauthentication": token }),
      ...headers,
    },
    body: JSON.stringify(update),
  });

  if (!response.ok) {
    throw await apiErrorFrom(response);
  }

  return (await response.json()) as SettingsSectionView<T>;
}

// A save answers with the whole section, and the hub's settingsChanged carries it the same way.
export function putSection(queryClient: QueryClient, view: SettingsSectionView<unknown>): void {
  queryClient.setQueryData(settingsKey(view.section), view);
}

// What configuration alone decides, and a short state of every section.
export interface SettingsOverview {
  sections: {
    section: string;
    kind: "Live" | "Restart";
    version: number;
    updatedUtc: string | null;
    updatedBy: string | null;
    lockedCount: number;
    problemCount: number;
    apply: SettingsApplyState[] | null;
  }[];
  server: {
    key: string;
    value: string | null;
    isSet: boolean;
    source: string | null;
    secret: boolean;
  }[];
  keyRingReadable: boolean;
}

export const settingsOverviewQuery = queryOptions({
  queryKey: ["settings-overview"],
  queryFn: () => apiGet<SettingsOverview>("/api/settings"),
});

// The server certificate as the settings API shows it; its page reads it with this key, and the hub's
// certificateChanged replaces it.
export const certificateKey = ["settings-certificate"] as const;
