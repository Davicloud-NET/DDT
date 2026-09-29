// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { AdministratorsOnly } from "@/auth/AdministratorsOnly";
import type { CurrentUser } from "@/auth/auth";
import { Page } from "@/ui/Page";
import { PageHeader } from "@/ui/PageHeader";

import { DirectorySettings } from "./directory/DirectorySettings";
import { SingleSignOnSettings } from "./signIn/SingleSignOnSettings";

// Administration > Sign-in: who signs in besides the local accounts, and which role they get. Every field decides who
// reaches DDT, so only administrators can read and change them. For others the page asks the server nothing.
export function SignInSettingsPage() {
  return (
    <AdministratorsOnly
      title={<Trans>Sign-in</Trans>}
      refusal={
        <Trans>
          Only administrators change how people sign in. Your own password and second factor are on
          the Account and security page in the account menu.
        </Trans>
      }
      className="max-w-[72rem]"
    >
      {(me) => (
        <Page className="max-w-[72rem]">
          <PageHeader title={<Trans>Sign-in</Trans>} />
          <SignInSettings me={me} />
        </Page>
      )}
    </AdministratorsOnly>
  );
}

function SignInSettings({ me }: { me: CurrentUser }) {
  return (
    <>
      <p className="max-w-[80ch] text-ink-2">
        <Trans>
          Local accounts always sign in with their password. Here you let people sign in with their
          directory account or through a single sign-on provider as well, and decide which role
          their groups give them.
        </Trans>
      </p>
      <DirectorySettings me={me} />
      <SingleSignOnSettings />
    </>
  );
}
