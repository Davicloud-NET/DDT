// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import { useQuery } from "@tanstack/react-query";

import type { CurrentUser } from "@/auth/auth";
import { useNow } from "@/lib/useNow";
import { liveListOptions } from "@/live/freshness";
import { useLiveStatus } from "@/live/useLiveStatus";
import { Button } from "@/ui/Button";
import { EmptyState } from "@/ui/EmptyState";
import { ListSkeleton } from "@/ui/ListSkeleton";
import { Notice } from "@/ui/Notice";
import { Page } from "@/ui/Page";
import { PageHeader } from "@/ui/PageHeader";
import { Panel } from "@/ui/Panel";
import { SearchField } from "@/ui/SearchField";
import { useListSearch } from "@/ui/useListSearch";

import { DirectoryPanel } from "./directory/DirectoryPanel";
import { UserActionDialogs } from "./UserActionDialogs";
import { usersQuery } from "./users";
import { UsersTable } from "./UsersTable";
import { matchesUser } from "./userView";
import { useUserActions } from "./useUserActions";
import { useUserMarks } from "./useUserMarks";

// The Users page as an administrator sees it.
export function UserAdministration({ me }: { me: CurrentUser }) {
  const { t } = useLingui();
  const live = useLiveStatus();
  const users = useQuery({ ...usersQuery, ...liveListOptions(live) });
  const now = useNow(30_000);
  const mark = useUserMarks();
  const actions = useUserActions();
  const { enable } = actions;
  const all = users.data ?? [];
  const { query, setQuery, shown } = useListSearch(all, matchesUser);

  return (
    <Page>
      <PageHeader title={<Trans>Users and roles</Trans>}>
        <div className="flex-1" />
        {all.length > 0 ? (
          <SearchField
            label={t`Find an account`}
            placeholder={t`Name, user name or email`}
            value={query}
            onChange={setQuery}
          />
        ) : null}
        <Button
          variant="primary"
          onPress={() => {
            actions.setAdding(true);
          }}
        >
          <Trans>Add account</Trans>
        </Button>
      </PageHeader>

      <p className="max-w-[80ch] text-ink-2">
        <Trans>
          An administrator changes everything, including the accounts here; an operator approves
          machines and starts deployments; a viewer only looks. Directory and single sign-on
          accounts appear after their first sign-in.
        </Trans>
      </p>

      {users.isError ? (
        <Notice tone="fail">
          <Trans>The accounts could not be loaded.</Trans>
        </Notice>
      ) : null}
      {enable.isError ? <Notice tone="fail">{enable.error.message}</Notice> : null}

      <Panel title={<Trans>Accounts</Trans>} flush>
        {users.isPending ? (
          <ListSkeleton />
        ) : shown.length === 0 ? (
          <EmptyState title={<Trans>No account matches</Trans>} />
        ) : (
          <UsersTable
            users={shown}
            now={now}
            meId={me.id}
            isEnabling={enable.isPending}
            mark={mark}
            onAction={actions.pick}
          />
        )}
      </Panel>

      <DirectoryPanel />

      <UserActionDialogs actions={actions} meId={me.id} />
    </Page>
  );
}
