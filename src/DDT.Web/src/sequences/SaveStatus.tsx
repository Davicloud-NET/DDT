// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { AutosaveState } from "@/lib/autosave";
import { useNow } from "@/lib/useNow";

import styles from "./SaveStatus.module.scss";

export interface SaveStatusProps {
  state: AutosaveState;
  readOnly: boolean;
}

function describe(state: AutosaveState, now: number): string {
  switch (state.kind) {
    case "saved":
      return state.at === null
        ? "All changes saved"
        : `All changes saved at ${new Date(state.at).toLocaleTimeString()}`;
    case "pending":
      return "Unsaved changes";
    case "saving":
      return "Saving";
    case "retrying":
      return `Not saved: ${state.message} Retrying in ${String(Math.max(0, Math.ceil((state.nextAt - now) / 1000)))} s.`;
    case "refused":
      return `Not saved: ${state.message}`;
    case "conflict":
      return "Not saved: someone else saved this sequence";
    case "stopped":
      return `Not saved: ${state.message}`;
  }
}

export function SaveStatus({ state, readOnly }: SaveStatusProps) {
  // Counts down to the next attempt.
  const now = useNow(state.kind === "retrying" ? 1_000 : 60_000);
  const failing = ["retrying", "refused", "conflict", "stopped"].includes(state.kind);

  return (
    <p className={failing ? styles.failing : styles.status} role="status">
      {readOnly ? "Read only: only administrators change sequences." : describe(state, now)}
    </p>
  );
}
