// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { Notice } from "@/ui/Notice";
import { SecretValue } from "@/ui/SecretValue";

import type { CreatedApiToken } from "./tokens";

// A new token's secret and how a script sends it.
export function TokenSecret({ created }: { created: CreatedApiToken }) {
  return (
    <>
      <Notice tone="attention">
        <Trans>Shown only once.</Trans>
      </Notice>
      <SecretValue label={<Trans>Token</Trans>} value={created.secret} />
      <p>
        <Trans>
          A script sends it in the Authorization header: Bearer, a space, then the token.
        </Trans>
      </p>
    </>
  );
}
