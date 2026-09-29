// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { fullTime, relativeTimeAhead } from "@/lib/relativeTime";
import { Facts } from "@/ui/Facts";

import type { CertificateView } from "../certificate";

export function CertificateFacts({ view, now }: { view: CertificateView; now: number }) {
  const served = view.served;

  if (served === null) {
    return null;
  }

  const root = served.rootSubject;
  const renews = served.renewsUtc === null ? null : relativeTimeAhead(served.renewsUtc, now);
  const expires = relativeTimeAhead(served.notAfter, now);
  const until = fullTime(served.notAfter);

  return (
    <Facts
      items={[
        { label: <Trans>Subject</Trans>, value: served.subject, mono: true },
        { label: <Trans>Names</Trans>, value: served.names.join(", "), mono: true },
        {
          label: <Trans>Issued by</Trans>,
          value:
            served.managedByDdt && root !== null ? (
              <Trans>DDT's root, {root}</Trans>
            ) : (
              <Trans>Another root: an administrator's certificate, which DDT does not renew</Trans>
            ),
        },
        ...(served.rootSha256 === null
          ? []
          : [{ label: <Trans>Root SHA-256</Trans>, value: served.rootSha256, mono: true }]),
        { label: <Trans>SHA-256</Trans>, value: served.sha256, mono: true },
        {
          label: <Trans>Valid until</Trans>,
          value: (
            <>
              {until} <span className="text-muted">({expires})</span>
            </>
          ),
        },
        ...(renews === null
          ? []
          : [{ label: <Trans>Renewal</Trans>, value: <Trans>DDT renews it {renews}.</Trans> }]),
        ...(view.servedHere === null
          ? []
          : [
              {
                label: <Trans>This page</Trans>,
                value: view.servedHere ? (
                  <Trans>Was served this certificate.</Trans>
                ) : (
                  <Trans>Was served the certificate before; its next request gets this one.</Trans>
                ),
              },
            ]),
      ]}
    />
  );
}
