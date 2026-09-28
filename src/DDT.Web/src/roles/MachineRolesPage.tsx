// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { plural } from "@lingui/core/macro";
import { Trans, useLingui } from "@lingui/react/macro";
import { IconDots, IconPlus } from "@tabler/icons-react";
import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { Button as AriaButton, MenuTrigger } from "react-aria-components";

import { currentUserQuery } from "@/auth/auth";
import { liveListOptions } from "@/live/freshness";
import { useLiveMarks } from "@/live/useLiveMarks";
import { useLiveStatus } from "@/live/useLiveStatus";
import { rulesQuery } from "@/rules/rules";
import { Button } from "@/ui/Button";
import { cx } from "@/ui/cx";
import { EmptyState, Page, PageHeader, Panel, Skeleton } from "@/ui/Layout";
import { Menu, MenuItem } from "@/ui/Menu";
import { Notice } from "@/ui/Notice";
import { Table, TableBody, TableCell, TableColumn, TableHeader, TableRow } from "@/ui/Table";

import { DeleteRoleDialog, RoleDrawer, RuleLinks } from "./RoleDrawer";
import { machineRolesQuery, rulesGiving, type MachineRoleView } from "./roles";

// Machine roles, such as "Kiosk": values that rules give machines together, so one rule can say "these are kiosks"
// and the role says what a kiosk gets. Not the roles of users. An administrator adds and changes them in a drawer; a
// role that rules give cannot be deleted until they no longer give it. The list is live.
export function MachineRolesPage() {
  const { t } = useLingui();
  const freshness = liveListOptions(useLiveStatus());
  const roles = useQuery({ ...machineRolesQuery, ...freshness });
  const rules = useQuery({ ...rulesQuery, ...freshness });
  const user = useQuery(currentUserQuery).data ?? null;
  const canEdit = user?.roles.includes("Administrator") === true;
  const mark = useLiveMarks({
    queryKey: machineRolesQuery.queryKey,
    items: (list) => list,
    id: (role) => role.id,
    signature: (role) => String(role.revision),
    tone: () => "idle",
  });

  const [drawer, setDrawer] = useState<{ key: number; role: MachineRoleView | null } | null>(null);
  const [deleting, setDeleting] = useState<MachineRoleView | null>(null);

  const list = roles.data ?? [];
  const ruleList = rules.data ?? [];

  const open = (role: MachineRoleView | null) => {
    setDrawer((current) => ({ key: (current?.key ?? 0) + 1, role }));
  };

  return (
    <Page>
      <PageHeader title={<Trans>Machine roles</Trans>}>
        <div className="flex-1" />
        {canEdit ? (
          <Button
            variant="primary"
            onPress={() => {
              open(null);
            }}
          >
            <IconPlus aria-hidden="true" size={14} stroke={2} />
            <Trans>Add machine role</Trans>
          </Button>
        ) : null}
      </PageHeader>

      <p className="max-w-[80ch] text-ink-2">
        <Trans>
          A machine role is a set of values that rules give machines together, such as the time zone
          and the computer name of a kiosk. It says nothing about what a person may do; that is a
          user's role, under Administration.
        </Trans>
      </p>

      {roles.isError ? (
        <Notice tone="fail">
          <Trans>The machine roles could not be loaded.</Trans>
        </Notice>
      ) : null}

      {roles.isPending ? (
        <Panel>
          <Skeleton className="h-6 w-1/3" />
          <Skeleton className="h-6 w-2/3" />
        </Panel>
      ) : roles.isSuccess && list.length === 0 ? (
        <Panel>
          <EmptyState title={<Trans>No machine roles yet</Trans>}>
            {canEdit ? (
              <Trans>
                Add a machine role for values that belong together, then let a rule give it to the
                machines it matches.
              </Trans>
            ) : (
              <Trans>An administrator adds machine roles here.</Trans>
            )}
          </EmptyState>
        </Panel>
      ) : roles.isSuccess ? (
        <Panel flush>
          <Table
            aria-label={t`Machine roles`}
            className="min-w-[720px] table-fixed"
            onRowAction={(key) => {
              const role = list.find((candidate) => candidate.id === String(key));

              if (role !== undefined) {
                open(role);
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
            <TableBody items={list} dependencies={[mark, ruleList, canEdit]}>
              {(role) => {
                const giving = rulesGiving(ruleList, role.id);
                const count = role.values.length;

                return (
                  <TableRow
                    id={role.id}
                    textValue={role.name}
                    className={cx("h-auto", mark(role.id))}
                  >
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
                          {count === 0
                            ? t`No values`
                            : plural(count, { one: "# value", other: "# values" })}
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
                      {canEdit ? (
                        <RoleMenu
                          name={role.name}
                          onOpen={() => {
                            open(role);
                          }}
                          onDelete={() => {
                            setDeleting(role);
                          }}
                        />
                      ) : null}
                    </TableCell>
                  </TableRow>
                );
              }}
            </TableBody>
          </Table>
        </Panel>
      ) : null}

      {drawer !== null ? (
        <RoleDrawer
          key={drawer.key}
          role={drawer.role}
          rules={ruleList}
          canEdit={canEdit}
          onClose={() => {
            setDrawer(null);
          }}
        />
      ) : null}

      {deleting !== null ? (
        <DeleteRoleDialog
          role={deleting}
          rules={ruleList}
          onClose={() => {
            setDeleting(null);
          }}
          onDeleted={() => {
            setDeleting(null);
          }}
        />
      ) : null}
    </Page>
  );
}

function RoleMenu({
  name,
  onOpen,
  onDelete,
}: {
  name: string;
  onOpen: () => void;
  onDelete: () => void;
}) {
  const { t } = useLingui();
  const label = t`Actions for ${name}`;

  return (
    <MenuTrigger>
      <AriaButton
        aria-label={label}
        className="flex size-8 cursor-pointer items-center justify-center rounded-key text-muted key-motion outline-none hover:bg-hover pressed:bg-key-quiet-pressed hover:text-ink focus-visible:outline-2 focus-visible:outline-focus"
      >
        <IconDots size={18} stroke={2} />
      </AriaButton>
      <Menu
        aria-label={label}
        onAction={(key) => {
          if (key === "edit") {
            onOpen();
          } else {
            onDelete();
          }
        }}
      >
        <MenuItem id="edit">
          <Trans>Change</Trans>
        </MenuItem>
        <MenuItem id="delete" className="text-fail-text">
          <Trans>Delete</Trans>
        </MenuItem>
      </Menu>
    </MenuTrigger>
  );
}
