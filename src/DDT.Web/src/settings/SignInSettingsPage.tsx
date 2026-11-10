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
      refusal={<Trans>Only administrators change how people sign in.</Trans>}
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
      <DirectorySettings me={me} />
      <SingleSignOnSettings />
    </>
  );
}
