// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";

import { MenuItem } from "@/ui/Menu";
import { RowActionsMenu } from "@/ui/RowActionsMenu";

import type { UserView } from "./users";
import type { UserAction } from "./useUserActions";

// What an administrator can do to an account. The server refuses to disable, delete or reset the person asking, so
// those actions aren't offered on your own row. Your own password and second factor are on the Account page.
export function UserActionsMenu({
  user,
  isSelf,
  onAction,
}: {
  user: UserView;
  isSelf: boolean;
  onAction: (action: UserAction) => void;
}) {
  const { t } = useLingui();
  const name = user.userName;

  return (
    <RowActionsMenu
      label={t`Actions for ${name}`}
      onAction={(key) => {
        onAction(String(key) as UserAction);
      }}
    >
      <MenuItem id="change">
        <Trans>Change name, email or role</Trans>
      </MenuItem>
      {!isSelf && user.source === "Local" ? (
        <MenuItem id="reset-password">
          <Trans>Reset password</Trans>
        </MenuItem>
      ) : null}
      {!isSelf && user.twoFactorEnabled ? (
        <MenuItem id="reset-two-factor">
          <Trans>Reset second factor</Trans>
        </MenuItem>
      ) : null}
      {isSelf ? null : user.disabled ? (
        <MenuItem id="enable">
          <Trans>Enable</Trans>
        </MenuItem>
      ) : (
        <MenuItem id="disable">
          <Trans>Disable</Trans>
        </MenuItem>
      )}
      {!isSelf ? (
        <MenuItem id="delete" className="text-fail-text">
          <Trans>Delete</Trans>
        </MenuItem>
      ) : null}
    </RowActionsMenu>
  );
}
