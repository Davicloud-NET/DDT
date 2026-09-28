// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import { IconSearch } from "@tabler/icons-react";
import {
  Button as AriaButton,
  Dialog as AriaDialog,
  Modal as AriaModal,
  ModalOverlay,
} from "react-aria-components";

import { Palette } from "./palette/Palette";
import { usePaletteOpen } from "./palette/usePaletteOpen";

// A search over every page, machine, task sequence and image, to jump to it. What it lists is read when the palette
// first opens.
export function CommandPalette() {
  const { t } = useLingui();
  const { isOpen, setOpen } = usePaletteOpen();

  return (
    <>
      <AriaButton
        onPress={() => {
          setOpen(true);
        }}
        aria-keyshortcuts="Control+K"
        className="mr-3 flex h-8 w-56 cursor-pointer items-center gap-2 self-center rounded-key bg-frame-hover px-2.5 text-frame-muted motion-colors outline-none hover:text-frame-text focus-visible:outline-2 focus-visible:outline-focus max-md:w-auto"
      >
        <IconSearch aria-hidden="true" size={16} stroke={2} />
        <span className="flex-1 text-left type-small max-md:sr-only">
          <Trans>Go to…</Trans>
        </span>
        <kbd className="rounded-tag border border-frame-line px-1.5 font-sans type-small max-md:hidden">
          Ctrl K
        </kbd>
      </AriaButton>
      <ModalOverlay
        isOpen={isOpen}
        onOpenChange={setOpen}
        isDismissable
        className="fixed inset-0 z-50 flex items-start justify-center bg-backdrop px-4 pt-[14vh] entering:animate-overlay-in exiting:animate-overlay-out"
      >
        <AriaModal className="w-full max-w-150 overflow-hidden rounded-overlay bg-raised shadow-overlay outline-none entering:animate-pop-in exiting:animate-pop-out">
          <AriaDialog
            aria-label={t`Go to a page, machine, sequence or image`}
            className="outline-none"
          >
            <Palette
              onDone={() => {
                setOpen(false);
              }}
            />
          </AriaDialog>
        </AriaModal>
      </ModalOverlay>
    </>
  );
}
