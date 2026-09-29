// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useLingui } from "@lingui/react/macro";
import { IconX } from "@tabler/icons-react";
import type { ReactNode } from "react";
import {
  Button as AriaButton,
  Dialog as AriaDialog,
  Heading,
  Modal as AriaModal,
  ModalOverlay,
} from "react-aria-components";

import { cx } from "./cx";

export interface DialogProps {
  isOpen: boolean;
  onOpenChange: (open: boolean) => void;
  title: ReactNode;
  children: ReactNode;
  footer?: ReactNode;
  // A band above the title, for dialogs that erase something.
  hazard?: boolean;
  // While true, Escape and a click outside don't close it, for example during a save.
  isBusy?: boolean;
  width?: "md" | "lg";
  // The id of what a screen reader reads out with the title as the dialog opens.
  describedBy?: string;
}

// A modal over the dimmed page. React Aria traps the focus in it and keeps it mounted until its exit has run.
export function Dialog({
  isOpen,
  onOpenChange,
  title,
  children,
  footer,
  hazard = false,
  isBusy = false,
  width = "md",
  describedBy,
}: DialogProps) {
  const { t } = useLingui();

  return (
    <ModalOverlay
      isOpen={isOpen}
      onOpenChange={onOpenChange}
      isDismissable={!isBusy}
      isKeyboardDismissDisabled={isBusy}
      className="fixed inset-0 z-40 flex items-start justify-center overflow-y-auto bg-backdrop px-4 pt-[12vh] pb-8 entering:animate-overlay-in exiting:animate-overlay-out"
    >
      <AriaModal
        className={cx(
          "w-full overflow-hidden rounded-overlay bg-raised shadow-overlay outline-none entering:animate-pop-in exiting:animate-pop-out",
          width === "md" ? "max-w-130" : "max-w-180",
        )}
      >
        <AriaDialog
          {...(describedBy === undefined ? {} : { "aria-describedby": describedBy })}
          className="flex flex-col outline-none"
        >
          {hazard ? <span aria-hidden="true" className="block h-3 hazard-band" /> : null}
          <div className="flex items-start gap-3 px-5 pt-5">
            <Heading slot="title" className="flex-1 type-heading text-ink">
              {title}
            </Heading>
            <AriaButton
              slot="close"
              aria-label={t`Close`}
              isDisabled={isBusy}
              className="-mt-1 -mr-1 flex size-8 cursor-pointer items-center justify-center rounded-key text-muted key-motion outline-none hover:bg-hover hover:text-ink pressed:bg-key-quiet-pressed focus-visible:outline-2 focus-visible:outline-focus disabled:opacity-40"
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
