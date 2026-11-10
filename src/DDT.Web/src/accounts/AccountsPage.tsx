// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { IconPlus } from "@tabler/icons-react";

import { Button } from "@/ui/Button";
import { EmptyState } from "@/ui/EmptyState";
import { ListSkeleton } from "@/ui/ListSkeleton";
import { Notice } from "@/ui/Notice";
import { Page } from "@/ui/Page";
import { PageHeader } from "@/ui/PageHeader";
import { Panel } from "@/ui/Panel";

import { AccountDrawer } from "./AccountDrawer";
import { DeleteAccountDialog } from "./DeleteAccountDialog";
import { AccountsTable } from "./list/AccountsTable";
import { useAccountsPage } from "./useAccountsPage";

// The accounts that steps use. Everyone signed in can read the list, but never a password. An administrator adds and
// changes accounts after entering their own password again.
export function AccountsPage() {
  const { accounts, list, canEdit, mark, drawer, deleting, open, close, setDeleting } =
    useAccountsPage();

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

      <Notice tone="info" className="max-w-[80ch]">
        <Trans>
          Every operator can obtain an account a sequence uses by running that sequence.
        </Trans>{" "}
        <Trans>Give each account the least it needs.</Trans>
      </Notice>

      {accounts.isError ? (
        <Notice tone="fail">
          <Trans>The accounts could not be loaded.</Trans>
        </Notice>
      ) : null}

      {accounts.isPending ? (
        <Panel>
          <ListSkeleton widths={["w-1/3", "w-2/3"]} padded={false} />
        </Panel>
      ) : accounts.isSuccess && list.length === 0 ? (
        <Panel>
          <EmptyState title={<Trans>No accounts yet</Trans>} />
        </Panel>
      ) : accounts.isSuccess ? (
        <Panel flush>
          <AccountsTable
            list={list}
            canEdit={canEdit}
            mark={mark}
            onOpen={open}
            onDelete={setDeleting}
          />
        </Panel>
      ) : null}

      {drawer !== null ? (
        <AccountDrawer key={drawer.key} account={drawer.account} onClose={close} />
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
