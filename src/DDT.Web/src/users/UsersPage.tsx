// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { AdministratorsOnly } from "@/auth/AdministratorsOnly";

import { UserAdministration } from "./UserAdministration";

// Administration > Users and roles. Lists every account DDT knows, with its role and where that role comes from, plus
// the directory's group map. Only administrators can use it, and the server refuses everyone else.
export function UsersPage() {
  return (
    <AdministratorsOnly
      title={<Trans>Users and roles</Trans>}
      refusal={
        <Trans>
          Only administrators manage accounts. Your own account is on the Account and security page
          in the account menu.
        </Trans>
      }
    >
      {(me) => <UserAdministration me={me} />}
    </AdministratorsOnly>
  );
}
