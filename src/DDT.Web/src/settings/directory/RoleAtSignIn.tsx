// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { roleLabel } from "@/users/userView";

// The role the tested user's groups give. Without one, a map with entries refuses the sign-in, and an empty map
// leaves the role to the Users and roles page.
export function RoleAtSignIn({ role, decides }: { role: string | null; decides: boolean }) {
  if (role !== null) {
    return <span className="type-label">{roleLabel(role)}</span>;
  }

  return decides ? (
    <span className="text-attention-text">
      <Trans>None, so the sign-in is refused</Trans>
    </span>
  ) : (
    <span className="text-muted">
      <Trans>Chosen on the Users and roles page</Trans>
    </span>
  );
}
