// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { AutosaveState } from "@/lib/autosave";

import styles from "./AutosaveStatus.module.scss";

// The save state of one row that saves itself, in a few words. Nothing is shown while all is saved.
export function AutosaveStatus({ state }: { state: AutosaveState }) {
  switch (state.kind) {
    case "saved":
      return state.at === null ? null : (
        <span className={styles.status} role="status">
          Saved
        </span>
      );
    case "pending":
      return (
        <span className={styles.status} role="status">
          Unsaved changes
        </span>
      );
    case "saving":
      return (
        <span className={styles.status} role="status">
          Saving
        </span>
      );
    case "retrying":
      return (
        <span className={styles.failing} role="status">
          {`Not saved: ${state.message} Trying again.`}
        </span>
      );
    // A refusal of one field is shown at that field.
    case "refused":
      return (
        <span className={styles.failing} role="status">
          {Object.keys(state.problem?.errors ?? {}).length > 0
            ? "Not saved"
            : `Not saved: ${state.message}`}
        </span>
      );
    case "stopped":
      return (
        <span className={styles.failing} role="status">
          {`Not saved: ${state.message}`}
        </span>
      );
    case "conflict":
      return (
        <span className={styles.failing} role="status">
          Not saved: someone else changed it
        </span>
      );
  }
}
