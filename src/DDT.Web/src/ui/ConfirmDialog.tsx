// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { useId, useState, type ReactNode } from "react";

import { Button } from "./Button";
import { Dialog } from "./Dialog";
import { Notice } from "./Notice";
import { TextField } from "./TextField";

export interface ConfirmDialogProps {
  isOpen: boolean;
  onOpenChange: (open: boolean) => void;
  title: ReactNode;
  // What happens, in plain words, and what cannot be undone.
  children: ReactNode;
  confirmLabel: ReactNode;
  onConfirm: () => void;
  // Actions that remove or stop something use the danger key.
  danger?: boolean;
  isBusy?: boolean;
  error?: ReactNode;
  // A word to type before the confirm key works, such as ERASE. The dialog then carries the hazard band.
  typedWord?: string;
  // Keeps the confirm key off for a reason the dialog's content explains, such as an allowance not given yet.
  isConfirmDisabled?: boolean;
}

// Cancel comes first and takes the focus, so Enter never confirms by accident; with a typedWord, its field does.
// The content is the dialog's aria-describedby, because the focus lands on a key rather than on the text.
export function ConfirmDialog({
  isOpen,
  onOpenChange,
  title,
  children,
  confirmLabel,
  onConfirm,
  danger = false,
  isBusy = false,
  error,
  typedWord,
  isConfirmDisabled = false,
}: ConfirmDialogProps) {
  const [typed, setTyped] = useState("");
  const descriptionId = useId();
  const ready = (typedWord === undefined || typed === typedWord) && !isConfirmDisabled;

  function openChange(open: boolean) {
    if (!open) {
      setTyped("");
    }

    onOpenChange(open);
  }

  return (
    <Dialog
      isOpen={isOpen}
      onOpenChange={openChange}
      title={title}
      hazard={typedWord !== undefined}
      isBusy={isBusy}
      describedBy={descriptionId}
      footer={
        <>
          <Button
            variant="secondary"
            autoFocus={typedWord === undefined}
            isDisabled={isBusy}
            onPress={() => {
              openChange(false);
            }}
          >
            <Trans>Cancel</Trans>
          </Button>
          <Button
            variant={danger ? "danger" : "primary"}
            isDisabled={isBusy || !ready}
            className={
              danger && ready
                ? "bg-fail text-on-fail shadow-none hover:bg-fail pressed:bg-fail-pressed"
                : undefined
            }
            onPress={onConfirm}
          >
            {confirmLabel}
          </Button>
        </>
      }
    >
      <div id={descriptionId} className="flex flex-col gap-4">
        {children}
      </div>
      {typedWord !== undefined ? (
        <TextField
          label={<Trans>Type {typedWord} to go on</Trans>}
          value={typed}
          onChange={setTyped}
          mono
          autoFocus
          autoComplete="off"
          spellCheck="false"
        />
      ) : null}
      {error ? <Notice tone="fail">{error}</Notice> : null}
    </Dialog>
  );
}
