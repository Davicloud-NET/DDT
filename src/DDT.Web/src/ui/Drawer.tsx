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

// A panel that slides in from the right over the page, for details and forms that belong to the page underneath,
// such as a machine picked in a list on a narrow screen. It is an overlay, so it casts the overlay shadow. Being
// fixed to that edge, it is the one thing that travels further than the motion distance: in from the edge in slow,
// and back out in fast, as the dimming around it fades.
export function Drawer({
  isOpen,
  onOpenChange,
  title,
  children,
  footer,
}: {
  isOpen: boolean;
  onOpenChange: (open: boolean) => void;
  title: ReactNode;
  children: ReactNode;
  footer?: ReactNode;
}) {
  const { t } = useLingui();

  return (
    <ModalOverlay
      isOpen={isOpen}
      onOpenChange={onOpenChange}
      isDismissable
      className="fixed inset-0 z-40 flex justify-end bg-backdrop entering:animate-overlay-in exiting:animate-overlay-out"
    >
      <AriaModal className="h-full w-full max-w-110 bg-raised shadow-overlay outline-none entering:animate-drawer-in exiting:animate-drawer-out">
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
