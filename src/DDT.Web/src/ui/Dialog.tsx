// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import { IconX } from "@tabler/icons-react";
import { useState, type ReactNode } from "react";
import {
  Button as AriaButton,
  Dialog as AriaDialog,
  Heading,
  Modal as AriaModal,
  ModalOverlay,
} from "react-aria-components";

import { Button } from "./Button";
import { cx } from "./cx";
import { Notice } from "./Notice";
import { TextField } from "./TextField";

// A dialog floats over the page: the lightest surface, one of the few with a shadow, over a dimmed page. React Aria
// traps the focus inside, closes it with Escape and gives the focus back to what opened it.

export interface DialogProps {
  isOpen: boolean;
  onOpenChange: (open: boolean) => void;
  title: ReactNode;
  children: ReactNode;
  footer?: ReactNode;
  // A band above the title, for dialogs that erase something.
  hazard?: boolean;
  // While true, Escape and a click outside do not close it, as during a save.
  isBusy?: boolean;
  width?: "md" | "lg";
}

export function Dialog({
  isOpen,
  onOpenChange,
  title,
  children,
  footer,
  hazard = false,
  isBusy = false,
  width = "md",
}: DialogProps) {
  const { t } = useLingui();

  return (
    <ModalOverlay
      isOpen={isOpen}
      onOpenChange={onOpenChange}
      isDismissable={!isBusy}
      isKeyboardDismissDisabled={isBusy}
      className="fixed inset-0 z-40 flex items-start justify-center overflow-y-auto bg-backdrop px-4 pt-[12vh] pb-8 entering:animate-overlay-in"
    >
      <AriaModal
        className={cx(
          "w-full overflow-hidden rounded-overlay bg-raised shadow-overlay outline-none entering:animate-pop-in",
          width === "md" ? "max-w-130" : "max-w-180",
        )}
      >
        <AriaDialog className="flex flex-col outline-none">
          {hazard ? <span aria-hidden="true" className="block h-3 hazard-band" /> : null}
          <div className="flex items-start gap-3 px-5 pt-5">
            <Heading slot="title" className="flex-1 type-heading text-ink">
              {title}
            </Heading>
            <AriaButton
              slot="close"
              aria-label={t`Close`}
              isDisabled={isBusy}
              className="-mt-1 -mr-1 flex size-8 cursor-pointer items-center justify-center rounded-key text-muted outline-none hover:bg-hover hover:text-ink focus-visible:outline-2 focus-visible:outline-focus disabled:opacity-40"
            >
              <IconX size={18} stroke={2} />
            </AriaButton>
          </div>
          <div className="flex flex-col gap-4 px-5 pt-3 pb-5 text-ink-2">{children}</div>
          {footer ? (
            <div className="flex flex-wrap items-center justify-end gap-2 border-t border-line-soft bg-panel px-5 py-3.5">
              {footer}
            </div>
          ) : null}
        </AriaDialog>
      </AriaModal>
    </ModalOverlay>
  );
}

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
}

// Asks before an action. The safe way out comes first and holds the focus, so Enter never confirms by accident;
// with a word to type, the field takes the focus instead, since nothing confirms until the word is there.
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
}: ConfirmDialogProps) {
  const [typed, setTyped] = useState("");
  const ready = typedWord === undefined || typed === typedWord;

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
              danger && ready ? "bg-fail text-on-fail shadow-none hover:bg-fail" : undefined
            }
            onPress={onConfirm}
          >
            {confirmLabel}
          </Button>
        </>
      }
    >
      {children}
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
