// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";

import { fullTime } from "@/lib/relativeTime";
import { StateTag } from "@/ui/StateTag";

import type { UserView } from "./users";
import { isLockedOut } from "./userView";

export function UserStateCell({ user, now }: { user: UserView; now: number }) {
  const { t } = useLingui();
  const tags = [];

  if (user.disabled) {
    tags.push(
      <StateTag key="disabled" tone="retired">
        <Trans>Disabled</Trans>
      </StateTag>,
    );
  }

  if (user.lockedOutUntil !== null && isLockedOut(user, now)) {
    const until = fullTime(user.lockedOutUntil);

    tags.push(
      <span key="locked" title={t`Locked until ${until}`}>
        <StateTag tone="attention">
          <Trans>Locked</Trans>
        </StateTag>
      </span>,
    );
  }

  if (user.mustChangePassword) {
    tags.push(
      <StateTag key="password" tone="attention">
        <Trans>New password due</Trans>
      </StateTag>,
    );
  }

  if (tags.length === 0) {
    return (
      <span className="type-small text-muted">
        <Trans>Active</Trans>
      </span>
    );
  }

  return <span className="flex flex-wrap gap-1.5">{tags}</span>;
}
