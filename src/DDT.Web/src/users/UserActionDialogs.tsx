// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { AddUserDialog } from "./AddUserDialog";
import { ChangeUserDialog } from "./ChangeUserDialog";
import { ConfirmUserAction } from "./ConfirmUserAction";
import { PasswordDialog } from "./PasswordDialog";
import type { UserActions } from "./useUserActions";

// The dialogs the Users page's actions open.
export function UserActionDialogs({ actions, meId }: { actions: UserActions; meId: string }) {
  const { act, changing } = actions;

  return (
    <>
      {actions.adding ? (
        <AddUserDialog
          onClose={() => {
            actions.setAdding(false);
          }}
          onCreated={actions.created}
        />
      ) : null}

      {changing !== null ? (
        <ChangeUserDialog
          user={changing}
          isSelf={changing.id === meId}
          onClose={() => {
            actions.setChanging(null);
          }}
        />
      ) : null}

      <PasswordDialog
        shown={actions.password}
        onClose={() => {
          actions.setPassword(null);
        }}
      />

      <ConfirmUserAction
        request={actions.confirming}
        isBusy={act.isPending}
        error={act.isError ? act.error.message : undefined}
        onCancel={() => {
          actions.setConfirming(null);
        }}
        onConfirm={actions.confirm}
      />
    </>
  );
}
