// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useQuery } from "@tanstack/react-query";
import { useState } from "react";

import { equalJson } from "@/lib/equalJson";

import { fieldErrors } from "./fieldErrors";
import {
  settingsQuery,
  type SecretAction,
  type SettingsLock,
  type SettingsSectionName,
} from "./settings";
import { useSettingsSave } from "./useSettingsSave";
import { withValueAt } from "./valuePath";

// One settings section as a form. The draft belongs to the user until they save or discard it. A change another
// administrator saves meanwhile arrives through the hub. It replaces the form if nothing is unsaved, and
// otherwise it's announced so the user can show theirs instead.
export function useSettingsForm<T>(
  section: SettingsSectionName,
  // Extra headers a save of this section sends besides the proof of identity, such as the directory proof.
  headers: () => Record<string, string> = () => ({}),
) {
  const query = useQuery(settingsQuery<T>(section));
  const view = query.data ?? null;

  // The version the draft started from, and the draft itself. Null while the form shows what's stored.
  const [draft, setDraft] = useState<{ version: number; values: T } | null>(null);
  const [secrets, setSecrets] = useState<Record<string, SecretAction>>({});

  const values = draft?.values ?? view?.values ?? null;
  const secretsChanged = Object.values(secrets).some((secret) => secret.action !== "Keep");
  const dirty =
    view !== null && ((draft !== null && !equalJson(draft.values, view.values)) || secretsChanged);
  const changedElsewhere = dirty && draft !== null && draft.version !== view.version;

  const save = useSettingsSave<T>({
    section,
    update: () => {
      if (view === null || values === null) {
        throw new Error("Nothing to save.");
      }

      return { version: draft?.version ?? view.version, values, secrets };
    },
    headers,
    onSaved: () => {
      setDraft(null);
      setSecrets({});
    },
  });

  return {
    view,
    query,
    values,
    dirty,
    changedElsewhere,
    saving: save.pending,
    refusal: save.refusal,
    warnings: save.warnings,
    needsReauth: save.needsReauth,
    secrets,
    lockOf: (field: string): SettingsLock | null =>
      view?.locked.find((lock) => lock.field === field || field.startsWith(`${lock.field}.`)) ??
      null,
    fieldErrors: (field: string): string[] =>
      fieldErrors(save.refusal, draft === null ? (view?.problems ?? []) : [], field),
    change: (field: string, value: unknown) => {
      if (view === null || values === null) {
        return;
      }

      setDraft({
        version: draft?.version ?? view.version,
        values: withValueAt(values, field, value),
      });
      save.clearRefusal();
    },
    setSecret: (field: string, action: SecretAction) => {
      setSecrets((current) => ({ ...current, [field]: action }));
      save.clearRefusal();
    },
    save: save.start,
    // After the password is typed again, the same save is sent once more.
    retryAfterReauth: save.retryAfterReauth,
    cancelReauth: save.cancelReauth,
    confirmWarnings: save.confirmWarnings,
    cancelWarnings: save.cancelWarnings,
    discard: () => {
      setDraft(null);
      setSecrets({});
      save.clearRefusal();
    },
  };
}

export type SettingsForm<T> = ReturnType<typeof useSettingsForm<T>>;
