// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { AdministratorsOnly } from "@/auth/AdministratorsOnly";

import { AllTokens } from "./AllTokens";

// Administration > API tokens. Lists every user's tokens, to see which scripts reach DDT and to revoke any of
// them. Everyone makes their own tokens on the Account page, so none are made here.
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
