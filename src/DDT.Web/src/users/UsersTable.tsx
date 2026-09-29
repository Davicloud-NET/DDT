// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";

import { fullTime, relativeTime } from "@/lib/relativeTime";
import { Table, TableBody, TableCell, TableColumn, TableHeader, TableRow } from "@/ui/Table";

import { AccountCell } from "./AccountCell";
import { UserActionsMenu } from "./UserActionsMenu";
import type { UserView } from "./users";
import { UserStateCell } from "./UserStateCell";
import { roleLabel, roleOrigin, sourceLabel } from "./userView";
import type { UserAction } from "./useUserActions";

interface UsersTableProps {
  users: UserView[];
  now: number;
  meId: string;
  isEnabling: boolean;
  // The live mark class for each row.
  mark: (id: string) => string;
  onAction: (action: UserAction, user: UserView) => void;
}

export function UsersTable({ users, now, meId, isEnabling, mark, onAction }: UsersTableProps) {
  const { t } = useLingui();

  return (
    <Table aria-label={t`Accounts`} className="min-w-[1000px] table-fixed">
      <TableHeader>
        <TableColumn id="account" isRowHeader className="w-[22%] pl-4">
          <Trans>Account</Trans>
        </TableColumn>
        <TableColumn id="source" className="w-40">
          <Trans>Type</Trans>
        </TableColumn>
        <TableColumn id="role">
          <Trans>Role</Trans>
        </TableColumn>
        <TableColumn id="twoFactor" className="w-28">
          <Trans>Second factor</Trans>
        </TableColumn>
        <TableColumn id="state" className="w-44">
          <Trans>State</Trans>
        </TableColumn>
        <TableColumn id="signIn" className="w-32">
          <Trans>Last sign-in</Trans>
        </TableColumn>
        <TableColumn id="actions" className="w-12 pr-4">
          <span className="sr-only">
            <Trans>Actions</Trans>
          </span>
        </TableColumn>
      </TableHeader>
      <TableBody items={users} dependencies={[now, meId, isEnabling, mark]}>
        {(user) => (
          <TableRow id={user.id} textValue={user.userName} className={mark(user.id)}>
            <TableCell className="pl-4">
              <AccountCell user={user} isSelf={user.id === meId} />
            </TableCell>
            <TableCell className="type-small">
              <SourceCell user={user} />
            </TableCell>
            <TableCell className="type-small">
              <RoleCell user={user} />
            </TableCell>
            <TableCell className="type-small">
              {user.twoFactorEnabled ? (
                <span className="text-ink">
                  <Trans>On</Trans>
                </span>
              ) : (
                <span className="text-muted">
                  <Trans>Off</Trans>
                </span>
              )}
            </TableCell>
            <TableCell>
              <UserStateCell user={user} now={now} />
            </TableCell>
            <TableCell className="type-small whitespace-nowrap text-muted">
              <LastSignIn user={user} now={now} />
            </TableCell>
            <TableCell className="pr-4">
              <UserActionsMenu
                user={user}
                isSelf={user.id === meId}
                onAction={(action) => {
                  onAction(action, user);
                }}
              />
            </TableCell>
          </TableRow>
        )}
      </TableBody>
    </Table>
  );
}

function SourceCell({ user }: { user: UserView }) {
  const provider = user.externalProvider;

  return (
    <span className="flex min-w-0 flex-col">
      <span className="text-ink">{sourceLabel(user.source)}</span>
      {provider !== null ? (
        <span className="truncate text-muted">
          {user.source === "External" ? provider : <Trans>Linked to {provider}</Trans>}
        </span>
      ) : null}
    </span>
  );
}

function RoleCell({ user }: { user: UserView }) {
  return (
    <span className="flex min-w-0 flex-col">
      <span className={user.role === null ? "text-attention-text" : "text-ink"}>
        {user.role === null ? <Trans>No role</Trans> : roleLabel(user.role)}
      </span>
      <span className="truncate text-muted">{roleOrigin(user)}</span>
    </span>
  );
}

function LastSignIn({ user, now }: { user: UserView; now: number }) {
  return user.lastSignInUtc === null ? (
    <Trans>Never</Trans>
  ) : (
    <span title={fullTime(user.lastSignInUtc)}>{relativeTime(user.lastSignInUtc, now)}</span>
  );
}
