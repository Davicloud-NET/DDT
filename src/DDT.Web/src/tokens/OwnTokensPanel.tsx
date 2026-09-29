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
import { ListSkeleton } from "@/ui/ListSkeleton";
import { Notice } from "@/ui/Notice";
import { Panel } from "@/ui/Panel";
import { highestRole } from "@/users/userView";

import { MakeTokenDialog } from "./MakeTokenDialog";
import { RevokeTokenDialog } from "./RevokeTokenDialog";
import { ownTokensQuery, type ApiTokenView } from "./tokens";
import { TokenTable } from "./TokenTable";
import { byState } from "./tokenView";

// The signed-in user's API tokens on the Account page. The list is live. A token that's made, used or revoked, here or
// by an administrator, arrives through the hub.
export function OwnTokensPanel({ user }: { user: CurrentUser }) {
  const { t } = useLingui();
  const live = useLiveStatus();
  const tokens = useQuery({ ...ownTokensQuery, ...liveListOptions(live) });
  const now = useNow(30_000);
  const [making, setMaking] = useState(false);
  const [revoking, setRevoking] = useState<ApiTokenView | null>(null);
  const canMake = highestRole(user.roles) !== null;
  const list = byState(tokens.data ?? [], now);

  return (
    <Panel
      title={<Trans>API tokens</Trans>}
      flush
      actions={
        canMake ? (
          <Button
            size="sm"
            onPress={() => {
              setMaking(true);
            }}
          >
            <Trans>Make a token</Trans>
          </Button>
        ) : null
      }
    >
      {tokens.isPending ? (
        <ListSkeleton widths={["w-1/2"]} />
      ) : tokens.isError ? (
        <div className="p-4">
          <Notice tone="fail">
            <Trans>Your tokens could not be loaded.</Trans>
          </Notice>
        </div>
      ) : list.length === 0 ? (
        <EmptyState title={<Trans>No tokens yet</Trans>}>
          {canMake ? (
            <Trans>
              A token lets a script call DDT's API as you, with a role no higher than yours and for
              as long as you choose.
            </Trans>
          ) : (
            <Trans>Your account has no role, so it cannot have a token.</Trans>
          )}
        </EmptyState>
      ) : (
        <TokenTable
          tokens={list}
          now={now}
          showOwner={false}
          onRevoke={setRevoking}
          label={t`Your API tokens`}
        />
      )}

      {making ? (
        <MakeTokenDialog
          user={user}
          onClose={() => {
            setMaking(false);
          }}
        />
      ) : null}

      <RevokeTokenDialog
        token={revoking}
        me={user}
        onClose={() => {
          setRevoking(null);
        }}
      />
    </Panel>
  );
}
