// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import { useBlocker } from "@tanstack/react-router";

import type { AutosaveState } from "@/lib/autosave";
import { ConfirmDialog } from "@/ui/ConfirmDialog";

import type { SequenceEditorState } from "../../useSequenceEditor";

function leaveConsequence(state: AutosaveState): string {
  switch (state.kind) {
    case "conflict":
      return t`Someone else saved this sequence, so your changes since your last save are not saved. Leaving throws them away.`;
    case "retrying": {
      const message = state.message;

      return t`${message} Your latest changes are not saved, and leaving throws them away.`;
    }
    case "refused":
    case "stopped": {
      const message = state.message;

      return t`The server did not save your latest changes: ${message} Leaving throws them away.`;
    }
    default:
      return t`Your latest changes are not saved yet. Leaving throws them away.`;
  }
}

// Asks before the page is left with changes that cannot be saved.
export function LeaveDialog({ editor }: { editor: SequenceEditorState }) {
  // Leaving saves first and goes once that worked; it asks only when the changes cannot be saved. Choosing another
  // node stays on the page, and signing out ends the session a save needs, so neither is held up.
  const leaving = useBlocker({
    shouldBlockFn: async ({ current, next }) =>
      next.pathname !== current.pathname && next.routeId !== "/sign-in" && !(await editor.flush()),
    enableBeforeUnload: false,
    disabled: !editor.dirty,
    withResolver: true,
  });

  return (
    <ConfirmDialog
      isOpen={leaving.status === "blocked"}
      onOpenChange={(open) => {
        if (!open) {
          leaving.reset?.();
        }
      }}
      title={<Trans>Leave without saving?</Trans>}
      confirmLabel={<Trans>Leave without saving</Trans>}
      danger
      onConfirm={() => {
        leaving.proceed?.();
      }}
    >
      <p>{leaveConsequence(editor.state)}</p>
    </ConfirmDialog>
  );
}
