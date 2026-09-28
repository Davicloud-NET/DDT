// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { AdministratorsOnly } from "@/auth/AdministratorsOnly";

import { AllTokens } from "./AllTokens";

// Administration > API tokens: every user's tokens, to see what scripts reach DDT and to revoke any of them. Each
// person makes their own on the Account page, so none is made here.
export function TokensPage() {
  return (
    <AdministratorsOnly
      title={<Trans>API tokens</Trans>}
      refusal={
        <Trans>
          Only administrators see every token. Your own are on the Account and security page in the
          account menu.
        </Trans>
      }
    >
      {(me) => <AllTokens me={me} />}
    </AdministratorsOnly>
  );
}
