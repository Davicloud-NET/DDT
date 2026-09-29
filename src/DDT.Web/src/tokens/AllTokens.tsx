// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import { useQuery } from "@tanstack/react-query";
import { useState } from "react";

import type { CurrentUser } from "@/auth/auth";
import { useNow } from "@/lib/useNow";
import { liveListOptions } from "@/live/freshness";
import { useLiveStatus } from "@/live/useLiveStatus";
import { Button } from "@/ui/Button";
import { EmptyState } from "@/ui/EmptyState";
import { FilterSelector } from "@/ui/FilterSelector";
import { ListSkeleton } from "@/ui/ListSkeleton";
import { Notice } from "@/ui/Notice";
import { Page } from "@/ui/Page";
import { PageHeader } from "@/ui/PageHeader";
import { Panel } from "@/ui/Panel";
import { SearchField } from "@/ui/SearchField";

import { RevokeTokenDialog } from "./RevokeTokenDialog";
import { allTokensQuery, type ApiTokenView } from "./tokens";
import { TokenTable } from "./TokenTable";
import { tokenFilters, type TokenFilter } from "./tokenView";
import { useTokenFilter } from "./useTokenFilter";

// The API tokens page as an administrator sees it.
export function AllTokens({ me }: { me: CurrentUser }) {
  const { i18n, t } = useLingui();
  const live = useLiveStatus();
  const tokens = useQuery({ ...allTokensQuery, ...liveListOptions(live) });
  const now = useNow(30_000);
  const [revoking, setRevoking] = useState<ApiTokenView | null>(null);
  const all = tokens.data ?? [];
  const list = useTokenFilter(all, now);

  return (
    <Page>
      <PageHeader title={<Trans>API tokens</Trans>}>
        <FilterSelector
          label={t`Show tokens by state`}
          selected={list.filter}
          onChange={(id) => {
            list.setFilter(id as TokenFilter);
          }}
          options={tokenFilters.map((option) => ({
            id: option.id,
            label: i18n._(option.label),
            count: list.count(option.id),
          }))}
        />
        <div className="flex-1" />
        <SearchField
          label={t`Find a token`}
          placeholder={t`Token or user name`}
          value={list.query}
          onChange={list.setQuery}
        />
      </PageHeader>

      <p className="max-w-[80ch] text-ink-2">
        <Trans>
          Each person makes their own tokens on the Account and security page, with a role no higher
          than theirs. A token acts for its user: it stops working when it expires, is revoked, or
          its user is disabled or loses the role.
        </Trans>
      </p>

      {tokens.isError ? (
        <Notice tone="fail">
          <Trans>The tokens could not be loaded.</Trans>
        </Notice>
      ) : null}

      <Panel flush>
        {tokens.isPending ? (
          <ListSkeleton />
        ) : all.length === 0 ? (
          <EmptyState title={<Trans>No tokens yet</Trans>}>
            <Trans>
              Nobody has made an API token. Scripts that call DDT use one each, made on the Account
              and security page of the account they act for.
            </Trans>
          </EmptyState>
        ) : list.shown.length === 0 ? (
          <EmptyState
            title={<Trans>No token matches</Trans>}
            action={
              <Button onPress={list.showAll}>
                <Trans>Show all tokens</Trans>
              </Button>
            }
          />
        ) : (
          <TokenTable
            tokens={list.shown}
            now={now}
            showOwner
            onRevoke={setRevoking}
            label={t`API tokens`}
          />
        )}
      </Panel>

      <RevokeTokenDialog
        token={revoking}
        me={me}
        onClose={() => {
          setRevoking(null);
        }}
      />
    </Page>
  );
}
