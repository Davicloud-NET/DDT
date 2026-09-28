// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";

import type { RuleView } from "@/rules/rules";
import { Table, TableBody, TableColumn, TableHeader } from "@/ui/Table";

import type { MachineRoleView } from "../roles";
import { RoleRow } from "./RoleRow";

interface RolesTableProps {
  list: readonly MachineRoleView[];
  rules: readonly RuleView[];
  canEdit: boolean;
  mark: (id: string) => string;
  onOpen: (role: MachineRoleView) => void;
  onDelete: (role: MachineRoleView) => void;
}

export function RolesTable({ list, rules, canEdit, mark, onOpen, onDelete }: RolesTableProps) {
  const { t } = useLingui();

  return (
    <Table
      aria-label={t`Machine roles`}
      className="min-w-[720px] table-fixed"
      onRowAction={(key) => {
        const role = list.find((candidate) => candidate.id === String(key));

        if (role !== undefined) {
          onOpen(role);
        }
      }}
    >
      <TableHeader>
        <TableColumn id="name" isRowHeader className="w-[32%] pl-4">
          <Trans>Machine role</Trans>
        </TableColumn>
        <TableColumn id="values">
          <Trans>Values</Trans>
        </TableColumn>
        <TableColumn id="given" className="w-[30%]">
          <Trans>Given by</Trans>
        </TableColumn>
        <TableColumn id="actions" className="w-14 pr-4">
          <span className="sr-only">
            <Trans>Actions</Trans>
          </span>
        </TableColumn>
      </TableHeader>
      <TableBody items={list} dependencies={[mark, rules, canEdit]}>
        {(role) => (
          <RoleRow
            role={role}
            rules={rules}
            canEdit={canEdit}
            mark={mark(role.id)}
            onOpen={() => {
              onOpen(role);
            }}
            onDelete={() => {
              onDelete(role);
            }}
          />
        )}
      </TableBody>
    </Table>
  );
}
