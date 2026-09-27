// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";

import { fullTime, relativeTime, relativeTimeAhead } from "@/lib/relativeTime";
import { Button } from "@/ui/Button";
import { StateTag } from "@/ui/StateTag";
import { Table, TableBody, TableCell, TableColumn, TableHeader, TableRow } from "@/ui/Table";
import { roleLabel } from "@/users/userView";

import type { ApiTokenView } from "./tokens";
import { tokenState } from "./tokenView";

// API tokens as the Account page lists a person's own and Administration lists everyone's. Only the last four
// characters of a secret are known; a token that still works can be revoked.
export function TokenTable({
  tokens,
  now,
  showOwner,
  onRevoke,
  label,
}: {
  tokens: ApiTokenView[];
  now: number;
  showOwner: boolean;
  onRevoke: (token: ApiTokenView) => void;
  label: string;
}) {
  return (
    <Table
      aria-label={label}
      className={showOwner ? "min-w-[1000px] table-fixed" : "min-w-[820px] table-fixed"}
    >
      <TableHeader>
        <TableColumn id="name" isRowHeader className="w-[22%] pl-4">
          <Trans>Token</Trans>
        </TableColumn>
        {showOwner ? (
          <TableColumn id="owner" className="w-36">
            <Trans>User</Trans>
          </TableColumn>
        ) : null}
        <TableColumn id="role" className="w-28">
          <Trans>Role</Trans>
        </TableColumn>
        <TableColumn id="used">
          <Trans>Last used</Trans>
        </TableColumn>
        <TableColumn id="expires" className="w-32">
          <Trans>Expires</Trans>
        </TableColumn>
        <TableColumn id="state" className="w-40">
          <Trans>State</Trans>
        </TableColumn>
        <TableColumn id="actions" className="w-24 pr-4">
          <span className="sr-only">
            <Trans>Actions</Trans>
          </span>
        </TableColumn>
      </TableHeader>
      <TableBody items={tokens} dependencies={[now, showOwner, onRevoke]}>
        {(token) => (
          <TableRow id={token.id} textValue={token.name}>
            <TableCell className="pl-4">
              <span className="flex min-w-0 flex-col">
                <span className="truncate type-label text-ink">{token.name}</span>
                <span className="truncate type-data text-[12.5px] text-muted">
                  ddt_…{token.hint}
                </span>
              </span>
            </TableCell>
            {showOwner ? (
              <TableCell className="type-small">
                <span className="truncate type-data text-[12.5px]">{token.userName}</span>
              </TableCell>
            ) : null}
            <TableCell className="type-small">{roleLabel(token.role)}</TableCell>
            <TableCell className="type-small">
              <LastUsed token={token} now={now} />
            </TableCell>
            <TableCell className="type-small whitespace-nowrap text-ink-2">
              <span title={fullTime(token.expiresUtc)}>
                {relativeTimeAhead(token.expiresUtc, now)}
              </span>
            </TableCell>
            <TableCell>
              <TokenStateCell token={token} now={now} />
            </TableCell>
            <TableCell className="pr-4 text-right">
              {tokenState(token, now) === "active" ? (
                <RevokeKey token={token} onRevoke={onRevoke} />
              ) : null}
            </TableCell>
          </TableRow>
        )}
      </TableBody>
    </Table>
  );
}

function RevokeKey({
  token,
  onRevoke,
}: {
  token: ApiTokenView;
  onRevoke: (token: ApiTokenView) => void;
}) {
  const { t } = useLingui();
  const name = token.name;

  return (
    <Button
      size="sm"
      variant="danger"
      aria-label={t`Revoke ${name}`}
      onPress={() => {
        onRevoke(token);
      }}
    >
      <Trans>Revoke</Trans>
    </Button>
  );
}

function LastUsed({ token, now }: { token: ApiTokenView; now: number }) {
  if (token.lastUsedUtc === null) {
    return (
      <span className="text-muted">
        <Trans>Never</Trans>
      </span>
    );
  }

  const when = relativeTime(token.lastUsedUtc, now);
  const address = token.lastUsedAddress;

  return (
    <span title={fullTime(token.lastUsedUtc)} className="text-ink-2">
      {address === null ? (
        when
      ) : (
        <Trans>
          {when} from <span className="type-data text-[12.5px]">{address}</span>
        </Trans>
      )}
    </span>
  );
}

function TokenStateCell({ token, now }: { token: ApiTokenView; now: number }) {
  switch (tokenState(token, now)) {
    case "active":
      return (
        <StateTag tone="ok">
          <Trans>Active</Trans>
        </StateTag>
      );
    case "expired":
      return (
        <StateTag tone="retired">
          <Trans>Expired</Trans>
        </StateTag>
      );
    case "revoked": {
      const when = relativeTime(token.revokedUtc ?? token.expiresUtc, now);
      const by = token.revokedByName;

      return (
        <span className="flex min-w-0 flex-col gap-1">
          <StateTag tone="retired">
            <Trans>Revoked</Trans>
          </StateTag>
          <span
            className="truncate type-small text-muted"
            title={fullTime(token.revokedUtc ?? token.expiresUtc)}
          >
            {by === null ? (
              when
            ) : (
              <Trans>
                {when} by {by}
              </Trans>
            )}
          </span>
        </span>
      );
    }
  }
}
