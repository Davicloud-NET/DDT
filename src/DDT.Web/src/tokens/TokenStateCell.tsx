// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { fullTime, relativeTime } from "@/lib/relativeTime";
import { ChangedBy } from "@/ui/ChangedBy";
import { StateTag } from "@/ui/StateTag";

import type { ApiTokenView } from "./tokens";
import { tokenState } from "./tokenView";

export function TokenStateCell({ token, now }: { token: ApiTokenView; now: number }) {
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
          <ChangedBy
            when={when}
            by={by}
            title={fullTime(token.revokedUtc ?? token.expiresUtc)}
            className="truncate type-small text-muted"
          />
        </span>
      );
    }
  }
}
