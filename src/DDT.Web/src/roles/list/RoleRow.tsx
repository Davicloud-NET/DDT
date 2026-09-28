// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { plural } from "@lingui/core/macro";
import { Trans, useLingui } from "@lingui/react/macro";

import type { RuleView } from "@/rules/rules";
import { cx } from "@/ui/cx";
import { TableCell, TableRow } from "@/ui/Table";

import { rulesGiving, type MachineRoleView } from "../roles";
import { RuleLinks } from "../RuleLinks";
import { RoleMenu } from "./RoleMenu";

interface RoleRowProps {
  role: MachineRoleView;
  rules: readonly RuleView[];
  canEdit: boolean;
  // The live mark's classes.
  mark: string;
  onOpen: () => void;
  onDelete: () => void;
}

// A machine role with its values and the rules that give it.
export function RoleRow({ role, rules, canEdit, mark, onOpen, onDelete }: RoleRowProps) {
  const { t } = useLingui();
  const giving = rulesGiving(rules, role.id);
  const count = role.values.length;

  return (
    <TableRow id={role.id} textValue={role.name} className={cx("h-auto", mark)}>
      <TableCell className="py-3 pl-4">
        <span className="flex min-w-0 flex-col">
          <span className="truncate type-label">{role.name}</span>
          {role.description !== null ? (
            <span className="truncate type-small text-muted">{role.description}</span>
          ) : null}
        </span>
      </TableCell>
      <TableCell className="py-3">
        <span className="flex min-w-0 flex-col">
          <span className="type-small text-ink-2">
            {count === 0 ? t`No values` : plural(count, { one: "# value", other: "# values" })}
          </span>
          {count > 0 ? (
            <span className="truncate type-data text-muted">
              {role.values.map((value) => value.name).join(", ")}
            </span>
          ) : null}
        </span>
      </TableCell>
      <TableCell className="py-3 type-small">
        {giving.length === 0 ? (
          <span className="text-muted">
            <Trans>No rule</Trans>
          </span>
        ) : (
          <RuleLinks rules={giving} />
        )}
      </TableCell>
      <TableCell className="pr-4">
        {canEdit ? <RoleMenu name={role.name} onOpen={onOpen} onDelete={onDelete} /> : null}
      </TableCell>
    </TableRow>
  );
}
