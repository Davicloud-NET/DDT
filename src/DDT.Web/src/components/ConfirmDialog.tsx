// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { ReactNode } from "react";

import { Dialog } from "./Dialog";

import styles from "./ConfirmDialog.module.scss";

export interface ConfirmDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: string;
  // What happens when the action is confirmed, in plain sentences.
  consequence: string;
  confirmLabel: string;
  busy: boolean;
  error: string | null;
  onConfirm: () => void;
  // Anything to decide before confirming, such as a checkbox, and whether confirming waits for it.
  children?: ReactNode;
  confirmDisabled?: boolean;
}

// Asks before an action that cannot be undone. Focus starts on Close, so Enter alone never confirms.
export function ConfirmDialog({
  open,
  onOpenChange,
  title,
  consequence,
  confirmLabel,
  busy,
  error,
  onConfirm,
  children,
  confirmDisabled = false,
}: ConfirmDialogProps) {
  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        if (!busy) {
          onOpenChange(next);
        }
      }}
      title={title}
      description={consequence}
    >
      {children}
      {error !== null && (
        <p className={styles.error} role="alert">
          {error}
        </p>
      )}
      <div className={styles.actions}>
        <button
          type="button"
          className={styles.secondary}
          disabled={busy}
          onClick={() => {
            onOpenChange(false);
          }}
        >
          Close
        </button>
        <button
          type="button"
          className={styles.confirm}
          disabled={busy || confirmDisabled}
          onClick={onConfirm}
        >
          {confirmLabel}
        </button>
      </div>
    </Dialog>
  );
}
