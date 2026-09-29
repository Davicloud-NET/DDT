// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.
// Adapted from the slideout menu of Untitled UI React, Copyright (c) 2025 Untitled UI, MIT, see licenses/untitledui/LICENSE.

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

// A panel from the right edge for details and forms of the page underneath, such as a machine picked on a phone.
// It's fixed to that edge, so it's the only overlay that travels further than the motion distance.
export function Drawer({
  isOpen,
  onOpenChange,
  title,
  children,
  footer,
  wide = false,
}: {
  isOpen: boolean;
  onOpenChange: (open: boolean) => void;
  title: ReactNode;
  children: ReactNode;
  footer?: ReactNode;
  // Wider, for a form whose rows hold several fields side by side, such as a rule's conditions.
  wide?: boolean;
}) {
  const { t } = useLingui();

  return (
    <ModalOverlay
      isOpen={isOpen}
      onOpenChange={onOpenChange}
      isDismissable
      className="fixed inset-0 z-40 flex justify-end bg-backdrop entering:animate-overlay-in exiting:animate-overlay-out"
    >
      <AriaModal
        className={cx(
          "h-full w-full bg-raised shadow-overlay outline-none entering:animate-drawer-in exiting:animate-drawer-out",
          wide ? "max-w-130" : "max-w-110",
        )}
      >
        {" "}
        <AriaDialog className="flex h-full flex-col outline-none">
          <div className="flex items-start gap-3 border-b border-line-soft px-5 py-4">
            <Heading slot="title" className="flex-1 type-heading text-ink">
              {title}
            </Heading>
            <AriaButton
              slot="close"
              aria-label={t`Close`}
              className="-mt-1 -mr-1 flex size-8 cursor-pointer items-center justify-center rounded-key text-muted key-motion outline-none hover:bg-hover hover:text-ink pressed:bg-key-quiet-pressed focus-visible:outline-2 focus-visible:outline-focus"
            >
              <IconX size={18} stroke={2} />
            </AriaButton>
          </div>
          <div className="flex min-h-0 flex-1 flex-col gap-4 overflow-y-auto px-5 py-4">
            {children}
          </div>
          {footer ? (
            <div className="flex flex-wrap items-center gap-2 border-t border-line-soft bg-panel px-5 py-3.5">
              {footer}
            </div>
          ) : null}
        </AriaDialog>
      </AriaModal>
    </ModalOverlay>
  );
}
