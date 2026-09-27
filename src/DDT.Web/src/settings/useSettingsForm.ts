// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";

import { currentUserQuery } from "@/auth/auth";
import { equalJson } from "@/lib/equalJson";

import {
  putSection,
  refusalOf,
  saveSettings,
  settingsQuery,
  type SaveRefusal,
  type SecretAction,
  type SettingsFinding,
  type SettingsLock,
  type SettingsSectionName,
  type SettingsSectionView,
} from "./settings";

// A dotted path such as "domain.name" into a section's values.
export function valueAt(values: unknown, path: string): unknown {
  return path
    .split(".")
    .reduce<unknown>(
      (current, key) =>
        current !== null && typeof current === "object"
          ? (current as Record<string, unknown>)[key]
          : undefined,
      values,
    );
}

export function withValueAt<T>(values: T, path: string, value: unknown): T {
  const [head, ...rest] = path.split(".");
  const record = (values ?? {}) as Record<string, unknown>;

  if (head === undefined) {
    return values;
  }

  return {
    ...record,
    [head]: rest.length === 0 ? value : withValueAt(record[head], rest.join("."), value),
  } as T;
}

// One settings section as a form. The draft is the person's own until they save or discard it: a change another
// administrator saves meanwhile arrives through the hub, replaces the form while it has nothing unsaved, and is
// otherwise announced so the person can take theirs instead. A save goes through the server's checks in turn: its
// warnings are confirmed in a dialog, and fields that grant roles or trust ask for the password again first.
export function useSettingsForm<T>(
  section: SettingsSectionName,
  // Headers a save of this section carries besides the proof of identity, such as the directory test's proof.
  headers: () => Record<string, string> = () => ({}),
) {
  const queryClient = useQueryClient();
  const query = useQuery(settingsQuery<T>(section));
  const view = query.data ?? null;

  // The version the draft started from, and the draft itself; null while the form shows what is stored.
  const [draft, setDraft] = useState<{ version: number; values: T } | null>(null);
  const [secrets, setSecrets] = useState<Record<string, SecretAction>>({});
  const [refusal, setRefusal] = useState<SaveRefusal | null>(null);
  const [needsReauth, setNeedsReauth] = useState(false);
  const [warnings, setWarnings] = useState<SettingsFinding[] | null>(null);
  // The warnings this save has confirmed, which a save again after the password goes out with.
  const [confirmed, setConfirmed] = useState<string[]>([]);

  const values = draft?.values ?? view?.values ?? null;
  const secretsChanged = Object.values(secrets).some((secret) => secret.action !== "Keep");
  const dirty =
    view !== null && ((draft !== null && !equalJson(draft.values, view.values)) || secretsChanged);
  const changedElsewhere = dirty && draft !== null && draft.version !== view.version;

  const save = useMutation({
    mutationFn: (confirm: string[]) => {
      if (view === null || values === null) {
        throw new Error("Nothing to save.");
      }

      return saveSettings<T>(
        section,
        { version: draft?.version ?? view.version, values, secrets, confirm },
        headers(),
      );
    },
    onSuccess: (saved: SettingsSectionView<T>) => {
      putSection(queryClient, saved);
      setDraft(null);
      setSecrets({});
      setRefusal(null);
      setWarnings(null);
      setConfirmed([]);
    },
    onError: (error) => {
      const refused = refusalOf(error);

      if (refused.kind === "reauthenticate") {
        setWarnings(null);
        setNeedsReauth(true);
      } else if (
        refused.kind === "invalid" &&
        refused.confirm.length > 0 &&
        Object.keys(refused.fields).length === 0
      ) {
        setWarnings(refused.confirm);
      } else {
        setWarnings(null);
        setRefusal(refused);
      }
    },
  });

  const lockOf = (field: string): SettingsLock | null =>
    view?.locked.find((lock) => lock.field === field || field.startsWith(`${lock.field}.`)) ?? null;

  return {
    view,
    query,
    values,
    dirty,
    changedElsewhere,
    saving: save.isPending,
    refusal,
    warnings,
    needsReauth,
    secrets,
    lockOf,
    // Problems of the stored section, and the server's refusal of the last save, for one field.
    fieldErrors: (field: string): string[] => [
      ...(refusal?.kind === "invalid" ? (refusal.fields[field] ?? []) : []),
      ...(draft === null
        ? (view?.problems ?? []).filter((p) => p.field === field).map((p) => p.message)
        : []),
    ],
    change: (field: string, value: unknown) => {
      if (view === null || values === null) {
        return;
      }

      setDraft({
        version: draft?.version ?? view.version,
        values: withValueAt(values, field, value),
      });
      setRefusal(null);
    },
    setSecret: (field: string, action: SecretAction) => {
      setSecrets((current) => ({ ...current, [field]: action }));
      setRefusal(null);
    },
    save: () => {
      setConfirmed([]);
      save.mutate([]);
    },
    // After the password was typed again, the same save goes out once more.
    retryAfterReauth: () => {
      setNeedsReauth(false);
      save.mutate(confirmed);
    },
    cancelReauth: () => {
      setNeedsReauth(false);
    },
    confirmWarnings: () => {
      const codes = [
        ...confirmed,
        ...(warnings ?? [])
          .map((warning) => warning.code)
          .filter((code): code is string => code !== null),
      ];

      setConfirmed(codes);
      save.mutate(codes);
    },
    cancelWarnings: () => {
      setWarnings(null);
    },
    discard: () => {
      setDraft(null);
      setSecrets({});
      setRefusal(null);
    },
  };
}

export type SettingsForm<T> = ReturnType<typeof useSettingsForm<T>>;

// Whether the signed-in person may change settings. Operators may read the deployment and machine sections.
export function useCanChangeSettings(): boolean {
  const user = useQuery(currentUserQuery).data ?? null;

  return user?.roles.includes("Administrator") === true;
}
