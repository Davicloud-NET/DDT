// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { ReactElement, ReactNode } from "react";

import { Notice } from "@/ui/Notice";

import { SavedMeanwhile } from "./SavedMeanwhile";

interface DrawerNoticesProps {
  // The title of the notice that someone saved the thing meanwhile; null while nobody did.
  savedMeanwhile: string | null;
  // Says that someone deleted the thing meanwhile; null while nobody did.
  gone: ReactElement | null;
  // Refused fields, and for rules saved findings, that the form does not show.
  loose: readonly string[];
  // A refusal that names no field.
  refused: string | null;
  isBusy: boolean;
  onTakeTheirs: () => void;
  onKeepMine: () => void;
  // Notices of the thing itself, such as a rule's problems, which go before the refusals.
  children?: ReactNode;
}

// The notices over the form of a rule's, a machine role's or an account's drawer.
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
