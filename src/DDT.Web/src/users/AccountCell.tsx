// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import type { UserView } from "./users";
import { shownName } from "./userView";

export function AccountCell({ user, isSelf }: { user: UserView; isSelf: boolean }) {
  return (
    <span className="flex min-w-0 flex-col">
      <span className="flex min-w-0 items-baseline gap-2">
        <span className="truncate type-label text-ink">{shownName(user)}</span>
        {isSelf ? (
          <span className="shrink-0 type-small text-muted">
            <Trans>you</Trans>
          </span>
        ) : null}
      </span>
      <AccountLine user={user} />
    </span>
  );
}

// Under the shown name: the user name, when the name is not already it, and the email address.
function AccountLine({ user }: { user: UserView }) {
  const named = shownName(user) !== user.userName;

  if (!named && user.email === null) {
    return null;
  }

  return (
    <span className="truncate type-small text-muted">
      {named ? <span className="type-data text-[12.5px]">{user.userName}</span> : null}
      {named && user.email !== null ? " · " : null}
      {user.email}
    </span>
  );
}
