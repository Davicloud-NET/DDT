// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";

import { Table, TableBody, TableColumn, TableHeader } from "@/ui/Table";

import type { AccountView } from "../accounts";
import { AccountRow } from "./AccountRow";

interface AccountsTableProps {
  list: readonly AccountView[];
  canEdit: boolean;
  mark: (id: string) => string;
  onOpen: (account: AccountView) => void;
  onDelete: (account: AccountView) => void;
}

// The accounts. Only someone who may change them opens one, since the account's drawer has no read-only form.
export function AccountsTable({ list, canEdit, mark, onOpen, onDelete }: AccountsTableProps) {
  const { t } = useLingui();

  return (
    <Table
      aria-label={t`Accounts`}
      className="min-w-[860px] table-fixed"
      {...(canEdit
        ? {
            onRowAction: (key) => {
              const account = list.find((candidate) => candidate.id === String(key));

              if (account !== undefined) {
                onOpen(account);
              }
            },
          }
        : {})}
    >
      <TableHeader>
        <TableColumn id="name" isRowHeader className="w-[22%] pl-4">
          <Trans>Account</Trans>
        </TableColumn>
        <TableColumn id="domain" className="w-[14%]">
          <Trans>Domain</Trans>
        </TableColumn>
        <TableColumn id="hosts" className="w-[16%]">
          <Trans>Servers</Trans>
        </TableColumn>
        <TableColumn id="runAs" className="w-[8%]">
          <Trans>Run as</Trans>
        </TableColumn>
        <TableColumn id="password" className="w-[14%]">
          <Trans>Password</Trans>
        </TableColumn>
        <TableColumn id="usedBy">
          <Trans>Used by</Trans>
        </TableColumn>
        <TableColumn id="actions" className="w-14 pr-4">
          <span className="sr-only">
            <Trans>Actions</Trans>
          </span>
        </TableColumn>
      </TableHeader>
      <TableBody items={list} dependencies={[mark, canEdit]}>
        {(account) => (
          <AccountRow
            account={account}
            canEdit={canEdit}
            mark={mark(account.id)}
            onOpen={() => {
              onOpen(account);
            }}
            onDelete={() => {
              onDelete(account);
            }}
          />
        )}
      </TableBody>
    </Table>
  );
}
