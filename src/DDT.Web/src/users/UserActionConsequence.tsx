// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import type { ConfirmationRequest } from "./useUserActions";
import { shownName, userDeletionConsequence } from "./userView";

// What an action that needs confirmation does to the account, shown in the confirmation.
export function UserActionConsequence({ kind, user }: ConfirmationRequest) {
  const name = shownName(user);

  switch (kind) {
    case "disable":
      return (
        <p>
          <Trans>
            {name} is signed out within a minute and cannot sign in, and its API tokens stop
            working, until an administrator enables it again. Nothing is deleted.
          </Trans>
        </p>
      );
    case "reset-password":
      return (
        <p>
          <Trans>
            The current password stops working at once, and {name} is signed out within a minute.
            DDT shows the new password once; {name} replaces it at the next sign-in.
          </Trans>
        </p>
      );
    case "reset-two-factor":
      return (
        <p>
          <Trans>
            For an account that lost its authenticator. {name} is signed out within a minute and
            signs in with the password alone until it sets up an authenticator again on its Account
            page.
          </Trans>
        </p>
      );
    case "delete":
      return <p>{userDeletionConsequence(user)}</p>;
  }
}
