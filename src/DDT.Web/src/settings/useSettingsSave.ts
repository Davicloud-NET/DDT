// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";

import {
  putSection,
  refusalOf,
  saveSettings,
  type SaveRefusal,
  type SettingsFinding,
  type SettingsSectionName,
  type SettingsSectionUpdate,
  type SettingsSectionView,
} from "./settings";

interface SettingsSaveOptions<T> {
  section: SettingsSectionName;
  // The draft to send. It throws while there's nothing to save, which fails the save.
  update: () => Omit<SettingsSectionUpdate<T>, "confirm">;
  headers: () => Record<string, string>;
  // Called once the saved section is in the cache.
  onSaved: () => void;
}

// Saves a section through the server's checks. Warnings wait for a confirmation, and fields that grant roles or
// trust ask for the password again first. Any other refusal stays until the next change.
export function useSettingsSave<T>({ section, update, headers, onSaved }: SettingsSaveOptions<T>) {
  const queryClient = useQueryClient();
  const [refusal, setRefusal] = useState<SaveRefusal | null>(null);
  const [needsReauth, setNeedsReauth] = useState(false);
  const [warnings, setWarnings] = useState<SettingsFinding[] | null>(null);
  // The warnings this save has confirmed. The resend after the password includes them again.
  const [confirmed, setConfirmed] = useState<string[]>([]);

  const save = useMutation({
    mutationFn: (confirm: string[]) =>
      saveSettings<T>(section, { ...update(), confirm }, headers()),
    onSuccess: (saved: SettingsSectionView<T>) => {
      putSection(queryClient, saved);
      onSaved();
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

  return {
    pending: save.isPending,
    refusal,
    warnings,
    needsReauth,
    clearRefusal: () => {
      setRefusal(null);
    },
    start: () => {
      setConfirmed([]);
      save.mutate([]);
    },
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
  };
}
