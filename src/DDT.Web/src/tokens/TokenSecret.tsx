// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { Notice } from "@/ui/Notice";
import { SecretValue } from "@/ui/SecretValue";
import { roleLabel } from "@/users/userView";

import type { CreatedApiToken } from "./tokens";

// A new token's secret and how a script sends it.
export function TokenSecret({ created }: { created: CreatedApiToken }) {
  const tokenRole = roleLabel(created.token.role);

  return (
    <>
      <Notice tone="attention">
        <Trans>
          DDT shows this token only now and keeps nothing but a hash of it. If it is lost, revoke it
          and make a new one.
        </Trans>
      </Notice>
      <SecretValue label={<Trans>Token</Trans>} value={created.secret} />
      <p>
        <Trans>
          A script sends it in the Authorization header: Bearer, a space, then the token. It has the
          role {tokenRole}, and never more rights than your account has.
        </Trans>
      </p>
    </>
  );
}
