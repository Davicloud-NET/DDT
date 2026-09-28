// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { useQuery } from "@tanstack/react-query";

import { currentUserQuery } from "@/auth/auth";
import { OwnTokensPanel } from "@/tokens/OwnTokensPanel";
import { Notice } from "@/ui/Notice";
import { Page } from "@/ui/Page";
import { PageHeader } from "@/ui/PageHeader";

import { AuthenticatorPanel } from "./AuthenticatorPanel";
import { PasswordPanel } from "./PasswordPanel";
import { ProfilePanel } from "./ProfilePanel";

// The signed-in person's own account, password, authenticator and API tokens. An account that must replace a password
// it was given sees only this page: the server answers nothing else, and the shell keeps it here.
export function AccountPage() {
  const user = useQuery(currentUserQuery).data ?? null;

  if (user === null) {
    return null;
  }

  return (
    <Page className="max-w-[72rem]">
      <PageHeader title={<Trans>Account and security</Trans>} />
      {user.mustChangePassword ? (
        <Notice tone="attention" title={<Trans>Set a password of your own first</Trans>}>
          <Trans>
            You signed in with a password an administrator was shown. Replace it under Password;
            until then, DDT shows you nothing but this page.
          </Trans>
        </Notice>
      ) : null}
      <div className="grid items-start gap-4 lg:grid-cols-2">
        <div className="flex flex-col gap-4">
          <ProfilePanel user={user} />
          <PasswordPanel user={user} />
        </div>
        <AuthenticatorPanel user={user} />
      </div>
      {user.mustChangePassword ? null : <OwnTokensPanel user={user} />}
    </Page>
  );
}
