// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";

import type { CurrentUser } from "@/auth/auth";
import { Facts } from "@/ui/Facts";
import { Panel } from "@/ui/Panel";
import { roleLabel, sourceLabel } from "@/users/userView";

// Who the signed-in user is: the name, the kind of account and the roles.
export function ProfilePanel({ user }: { user: CurrentUser }) {
  const roles = user.roles.map(roleLabel).join(", ");

  return (
    <Panel title={<Trans>You</Trans>}>
      <Facts
        items={[
          { label: <Trans>User name</Trans>, value: user.userName, mono: true },
          ...(user.displayName === null
            ? []
            : [{ label: <Trans>Name</Trans>, value: user.displayName }]),
          { label: <Trans>Account</Trans>, value: sourceLabel(user.source) },
          { label: <Trans>Role</Trans>, value: roles === "" ? t`None` : roles },
        ]}
      />
    </Panel>
  );
}
