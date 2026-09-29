// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { cx } from "@/ui/cx";
import { StateTag } from "@/ui/StateTag";
import { TableCell, TableRow } from "@/ui/Table";

import type { AccountView } from "../accounts";
import { AccountMenu } from "./AccountMenu";
import { UsedBy } from "./UsedBy";

interface AccountRowProps {
  account: AccountView;
  canEdit: boolean;
  // The classes that mark the row when a live change arrives.
  mark: string;
  onOpen: () => void;
  onDelete: () => void;
}

function PasswordState({ account }: { account: AccountView }) {
  if (account.password.unreadable) {
    return (
      <span className="type-small text-fail-text">
        <Trans>Cannot be read, enter it again</Trans>
      </span>
    );
  }

  return account.password.isSet ? (
    <StateTag tone="ok">
      <Trans>Set</Trans>
    </StateTag>
  ) : (
    <StateTag tone="idle">
      <Trans>Not set</Trans>
    </StateTag>
  );
}

// An account with where it may be used, whether its password is set and what uses it.
export function AccountRow({ account, canEdit, mark, onOpen, onDelete }: AccountRowProps) {
  return (
    <TableRow id={account.id} textValue={account.name} className={cx("h-auto", mark)}>
      <TableCell className="py-3 pl-4">
        <span className="flex min-w-0 flex-col">
          <span className="truncate type-label">{account.name}</span>
          <span className="truncate type-data text-ink-2">{account.userName}</span>
        </span>
      </TableCell>
      <TableCell className="py-3">
        {account.domain === null ? (
          <span className="type-small text-muted">
            <Trans>None</Trans>
          </span>
        ) : (
          <span className="block truncate type-data">{account.domain}</span>
        )}
      </TableCell>
      <TableCell className="py-3">
        {account.hosts.length === 0 ? (
          <span className="type-small text-muted">
            <Trans>None</Trans>
          </span>
        ) : (
          <span className="flex min-w-0 flex-col">
            {account.hosts.map((host) => (
              <span key={host} className="truncate type-data">
                {host}
              </span>
            ))}
          </span>
        )}
      </TableCell>
      <TableCell className="py-3 type-small">
        {account.runAs ? (
          <Trans>Allowed</Trans>
        ) : (
          <span className="text-muted">
            <Trans>No</Trans>
          </span>
        )}
      </TableCell>
      <TableCell className="py-3">
        <PasswordState account={account} />
      </TableCell>
      <TableCell className="py-3 type-small">
        <UsedBy account={account} />
      </TableCell>
      <TableCell className="pr-4">
        {canEdit ? <AccountMenu name={account.name} onOpen={onOpen} onDelete={onDelete} /> : null}
      </TableCell>
    </TableRow>
  );
}
