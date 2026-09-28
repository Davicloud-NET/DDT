// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import { IconDots, IconPlus } from "@tabler/icons-react";
import { useQuery } from "@tanstack/react-query";
import { Link } from "@tanstack/react-router";
import { Fragment, useState } from "react";
import { Button as AriaButton, MenuTrigger } from "react-aria-components";

import { currentUserQuery } from "@/auth/auth";
import { liveListOptions } from "@/live/freshness";
import { useLiveMarks } from "@/live/useLiveMarks";
import { useLiveStatus } from "@/live/useLiveStatus";
import { Button } from "@/ui/Button";
import { cx } from "@/ui/cx";
import { EmptyState, Page, PageHeader, Panel, Skeleton } from "@/ui/Layout";
import { Menu, MenuItem } from "@/ui/Menu";
import { Notice } from "@/ui/Notice";
import { StateTag } from "@/ui/StateTag";
import { Table, TableBody, TableCell, TableColumn, TableHeader, TableRow } from "@/ui/Table";

import { AccountDrawer, DeleteAccountDialog } from "./AccountDrawer";
import { accountsQuery, type AccountStepUse, type AccountUse, type AccountView } from "./accounts";

// The accounts steps use: a script runs as one, a share is connected with one, a domain is joined with one. Everyone
// signed in reads the list, never a password; an administrator adds and changes them, with their own password entered
// again. The list is live, and says for each account which sequences and steps name it.
export function AccountsPage() {
  const { t } = useLingui();
  const accounts = useQuery({ ...accountsQuery, ...liveListOptions(useLiveStatus()) });
  const user = useQuery(currentUserQuery).data ?? null;
  const canEdit = user?.roles.includes("Administrator") === true;
  const mark = useLiveMarks({
    queryKey: accountsQuery.queryKey,
    items: (list) => list,
    id: (account) => account.id,
    signature: (account) =>
      `${String(account.revision)} ${String(account.password.isSet)} ${String(account.usedBy.length)}`,
    tone: () => "idle",
  });

  const [drawer, setDrawer] = useState<{ key: number; account: AccountView | null } | null>(null);
  const [deleting, setDeleting] = useState<AccountView | null>(null);
  const list = accounts.data ?? [];

  const open = (account: AccountView | null) => {
    setDrawer((current) => ({ key: (current?.key ?? 0) + 1, account }));
  };

  return (
    <Page>
      <PageHeader title={<Trans>Accounts</Trans>}>
        <div className="flex-1" />
        {canEdit ? (
          <Button
            variant="primary"
            onPress={() => {
              open(null);
            }}
          >
            <IconPlus aria-hidden="true" size={14} stroke={2} />
            <Trans>Add account</Trans>
          </Button>
        ) : null}
      </PageHeader>

      <p className="max-w-[80ch] text-ink-2">
        <Trans>
          The accounts steps use: a script runs as one, a share is connected with one, a domain is
          joined with one. Each password is stored encrypted and goes only to the step that uses it,
          while the step runs; this page never shows it.
        </Trans>
      </p>

      <Notice tone="info" className="max-w-[80ch]">
        <Trans>
          Every operator can obtain an account a sequence uses by running that sequence on a machine
          they control.
        </Trans>{" "}
        <Trans>
          Give each account the least it needs, and prefer an account asked for when the run starts,
          which is not stored, to one kept here.
        </Trans>
      </Notice>

      {accounts.isError ? (
        <Notice tone="fail">
          <Trans>The accounts could not be loaded.</Trans>
        </Notice>
      ) : null}

      {accounts.isPending ? (
        <Panel>
          <Skeleton className="h-6 w-1/3" />
          <Skeleton className="h-6 w-2/3" />
        </Panel>
      ) : accounts.isSuccess && list.length === 0 ? (
        <Panel>
          <EmptyState title={<Trans>No accounts yet</Trans>}>
            {canEdit ? (
              <Trans>
                Add an account for a step that runs a script as someone, connects a share or joins a
                domain.
              </Trans>
            ) : (
              <Trans>An administrator adds accounts here.</Trans>
            )}
          </EmptyState>
        </Panel>
      ) : accounts.isSuccess ? (
        <Panel flush>
          <Table
            aria-label={t`Accounts`}
            className="min-w-[860px] table-fixed"
            {...(canEdit
              ? {
                  onRowAction: (key) => {
                    const account = list.find((candidate) => candidate.id === String(key));

                    if (account !== undefined) {
                      open(account);
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
                <TableRow
                  id={account.id}
                  textValue={account.name}
                  className={cx("h-auto", mark(account.id))}
                >
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
                    {canEdit ? (
                      <AccountMenu
                        name={account.name}
                        onOpen={() => {
                          open(account);
                        }}
                        onDelete={() => {
                          setDeleting(account);
                        }}
                      />
                    ) : null}
                  </TableCell>
                </TableRow>
              )}
            </TableBody>
          </Table>
        </Panel>
      ) : null}

      {drawer !== null ? (
        <AccountDrawer
          key={drawer.key}
          account={drawer.account}
          onClose={() => {
            setDrawer(null);
          }}
        />
      ) : null}

      {deleting !== null ? (
        <DeleteAccountDialog
          account={deleting}
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

// A sequence's steps that name the account, each once though it may name it twice, to run as and for a share.
function stepsOf(use: AccountUse): AccountStepUse[] {
  return (use.steps ?? []).filter(
    (step, index, all) => all.findIndex((other) => other.stepId === step.stepId) === index,
  );
}

// The sequences that name the account, each with the steps that do, which open the sequence at that step.
function UsedBy({ account }: { account: AccountView }) {
  if (account.usedBy.length === 0) {
    return (
      <span className="text-muted">
        <Trans>No sequence</Trans>
      </span>
    );
  }

  return (
    <ul className="flex min-w-0 flex-col gap-0.5">
      {account.usedBy.map((use) => (
        <li key={use.sequenceId} className="min-w-0">
          <Link
            to="/deployment/sequences/$sequenceId"
            params={{ sequenceId: use.sequenceId }}
            className="text-ink hover:underline"
          >
            {use.sequenceName}
          </Link>
          {stepsOf(use).length > 0 ? (
            <span className="text-muted">
              {": "}
              {stepsOf(use).map((step, index) => (
                <Fragment key={step.stepId}>
                  {index > 0 ? ", " : null}
                  <Link
                    to="/deployment/sequences/$sequenceId"
                    params={{ sequenceId: use.sequenceId }}
                    search={{ step: step.stepId }}
                    className="text-ink-2 hover:underline"
                  >
                    {step.stepName}
                  </Link>
                </Fragment>
              ))}
            </span>
          ) : null}
        </li>
      ))}
    </ul>
  );
}

function AccountMenu({
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
