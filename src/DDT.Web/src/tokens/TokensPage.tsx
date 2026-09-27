// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import { useQuery } from "@tanstack/react-query";
import { useState } from "react";

import { currentUserQuery, type CurrentUser } from "@/auth/auth";
import { useNow } from "@/lib/useNow";
import { liveListOptions } from "@/live/freshness";
import { useLiveStatus } from "@/live/useLiveStatus";
import { Button } from "@/ui/Button";
import { FilterSelector, SearchField } from "@/ui/Controls";
import { EmptyState, Page, PageHeader, Panel, Skeleton } from "@/ui/Layout";
import { Notice } from "@/ui/Notice";

import { RevokeTokenDialog } from "./RevokeTokenDialog";
import { TokenTable } from "./TokenTable";
import { allTokensQuery, type ApiTokenView } from "./tokens";
import { byState, inTokenFilter, matchesToken, tokenFilters, type TokenFilter } from "./tokenView";

// Administration > API tokens: every user's tokens, to see what scripts reach DDT and to revoke any of them. Each
// person makes their own on the Account page, so none is made here.
export function TokensPage() {
  const me = useQuery(currentUserQuery).data ?? null;

  if (me === null) {
    return null;
  }

  if (!me.roles.includes("Administrator")) {
    return (
      <Page>
        <PageHeader title={<Trans>API tokens</Trans>} />
        <Notice>
          <Trans>
            Only administrators see every token. Your own are on the Account and security page in
            the account menu.
          </Trans>
        </Notice>
      </Page>
    );
  }

  return <AllTokens me={me} />;
}

function AllTokens({ me }: { me: CurrentUser }) {
  const { i18n, t } = useLingui();
  const live = useLiveStatus();
  const tokens = useQuery({ ...allTokensQuery, ...liveListOptions(live) });
  const now = useNow(30_000);
  const [filter, setFilter] = useState<TokenFilter>("active");
  const [query, setQuery] = useState("");
  const [revoking, setRevoking] = useState<ApiTokenView | null>(null);

  const all = tokens.data ?? [];
  const needle = query.trim().toLowerCase();
  const matching = needle === "" ? all : all.filter((token) => matchesToken(token, needle));
  const shown = byState(
    matching.filter((token) => inTokenFilter(token, filter, now)),
    now,
  );

  return (
    <Page>
      <PageHeader title={<Trans>API tokens</Trans>}>
        <FilterSelector
          label={t`Show tokens by state`}
          selected={filter}
          onChange={(id) => {
            setFilter(id as TokenFilter);
          }}
          options={tokenFilters.map((option) => ({
            id: option.id,
            label: i18n._(option.label),
            count: matching.filter((token) => inTokenFilter(token, option.id, now)).length,
          }))}
        />
        <div className="flex-1" />
        <SearchField
          label={t`Find a token`}
          placeholder={t`Token or user name`}
          value={query}
          onChange={setQuery}
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
          <div className="flex flex-col gap-3 p-4">
            <Skeleton className="h-6 w-1/2" />
            <Skeleton className="h-6 w-2/3" />
          </div>
        ) : all.length === 0 ? (
          <EmptyState title={<Trans>No tokens yet</Trans>}>
            <Trans>
              Nobody has made an API token. Scripts that call DDT use one each, made on the Account
              and security page of the account they act for.
            </Trans>
          </EmptyState>
        ) : shown.length === 0 ? (
          <EmptyState
            title={<Trans>No token matches</Trans>}
            action={
              <Button
                onPress={() => {
                  setFilter("all");
                  setQuery("");
                }}
              >
                <Trans>Show all tokens</Trans>
              </Button>
            }
          />
        ) : (
          <TokenTable
            tokens={shown}
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
