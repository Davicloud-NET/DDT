// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { ReactElement, ReactNode } from "react";

import { Notice } from "@/ui/Notice";

import { SavedMeanwhile } from "./SavedMeanwhile";

interface DrawerNoticesProps {
  // The title of the notice that someone else saved the item in the meantime. Null if nobody did.
  savedMeanwhile: string | null;
  // Says that someone deleted the item in the meantime. Null if nobody did.
  gone: ReactElement | null;
  // Refused fields that the form doesn't show. For rules, also the saved findings it doesn't show.
  loose: readonly string[];
  // A refusal that names no field.
  refused: string | null;
  isBusy: boolean;
  onTakeTheirs: () => void;
  onKeepMine: () => void;
  // Notices about the item itself, such as a rule's problems. They go before the refusals.
  children?: ReactNode;
}

// The notices above the form in a rule's, machine role's or account's drawer.
export function DrawerNotices({
  savedMeanwhile,
  gone,
  loose,
  refused,
  isBusy,
  onTakeTheirs,
  onKeepMine,
  children,
}: DrawerNoticesProps) {
  return (
    <>
      {savedMeanwhile !== null ? (
        <SavedMeanwhile
          title={savedMeanwhile}
          isBusy={isBusy}
          onTakeTheirs={onTakeTheirs}
          onKeepMine={onKeepMine}
        />
      ) : null}

      {gone !== null ? <Notice tone="attention">{gone}</Notice> : null}

      {children}

      {loose.length > 0 ? <Notice tone="fail">{loose.join(" ")}</Notice> : null}
      {refused !== null ? <Notice tone="fail">{refused}</Notice> : null}
    </>
  );
}
