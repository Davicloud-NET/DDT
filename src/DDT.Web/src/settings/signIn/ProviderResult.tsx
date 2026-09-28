// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { Facts } from "@/ui/Facts";
import { ResultBox } from "@/ui/ResultBox";
import { StateTag } from "@/ui/StateTag";

import { settingsText } from "../settings";
import type { OidcTestResult } from "../signIn";

export function ProviderResult({ result }: { result: OidcTestResult }) {
  return (
    <ResultBox>
      <Facts
        items={[
          {
            label: <Trans>Provider</Trans>,
            value: (
              <StateTag tone={result.reached ? "ok" : "fail"}>
                {result.reached ? <Trans>Reached</Trans> : <Trans>Not reached</Trans>}
              </StateTag>
            ),
          },
          ...(result.issuer === null
            ? []
            : [{ label: <Trans>Issuer</Trans>, value: result.issuer, mono: true }]),
          { label: <Trans>Redirect URI</Trans>, value: result.redirectUri, mono: true },
        ]}
      />
      <p className="type-small text-ink">{settingsText(result)}</p>
    </ResultBox>
  );
}
