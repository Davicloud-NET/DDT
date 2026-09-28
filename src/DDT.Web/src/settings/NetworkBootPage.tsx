// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { AdministratorsOnly } from "@/auth/AdministratorsOnly";

import { NetworkBootSettings } from "./networkBoot/NetworkBootSettings";

// Boot > Network boot. The pxe section runs code on every machine that netboots, so only administrators read it; the
// server refuses everyone else.
export function NetworkBootPage() {
  return (
    <AdministratorsOnly
      title={<Trans>Network boot</Trans>}
      refusal={
        <Trans>
          Only administrators see and change network boot: which interfaces DDT answers on and what
          machines load.
        </Trans>
      }
    >
      {() => <NetworkBootSettings />}
    </AdministratorsOnly>
  );
}
