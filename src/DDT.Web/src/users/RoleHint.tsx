// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { Link } from "@tanstack/react-router";

import type { UserView } from "./users";
import type { RoleNote } from "./userView";

// The hint under an account's role in the change dialog.
export function RoleHint({ note, user }: { note: RoleNote; user: UserView }) {
  switch (note) {
    case "groups":
      return <RoleLockReason user={user} />;
    case "self":
      return <Trans>You cannot change your own role. Another administrator can.</Trans>;
    case "provisioned":
      return (
        <Trans>
          Single sign-on gave this role when it created the account. A role chosen here stays until
          an administrator changes it.
        </Trans>
      );
    case "none":
      return <Trans>The account has no role yet, so it reaches nothing.</Trans>;
  }
}

// Why the role cannot be chosen here, and where to change it instead.
function RoleLockReason({ user }: { user: UserView }) {
  const name = user.userName;

  return user.roleFrom === "SingleSignOnGroups" ? (
    <Trans>
      The role of {name} comes from its single sign-on groups, through the map on the{" "}
      <Link to="/admin/sign-in" className="underline">
        Sign-in
      </Link>{" "}
      page, at each sign-in. Change its groups at the provider, or the map.
    </Trans>
  ) : (
    <Trans>
      The role of {name} comes from its directory groups, through the map on the{" "}
      <Link to="/admin/sign-in" className="underline">
        Sign-in
      </Link>{" "}
      page, at each sign-in. Change its groups in the directory, or the map.
    </Trans>
  );
}
