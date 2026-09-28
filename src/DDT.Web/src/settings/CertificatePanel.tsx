// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { useQuery } from "@tanstack/react-query";

import { Notice } from "@/ui/Notice";
import { Skeleton } from "@/ui/Skeleton";

import { certificateQuery } from "./certificate";
import { ProvisionalNotice } from "./certificate/ProvisionalNotice";
import { ServedCertificate } from "./certificate/ServedCertificate";
import { ServerNames } from "./certificate/ServerNames";
import { settingsOverviewQuery } from "./settings";

// The served certificate, the names it must carry, and Generate and Upload to replace it. A new pair is on trial. If no
// connection that was served the new pair confirms it in time, DDT goes back to the previous pair, so nobody is locked
// out.
export function CertificatePanel() {
  const certificate = useQuery(certificateQuery);
  const overview = useQuery(settingsOverviewQuery);

  if (certificate.isError) {
    return (
      <Notice tone="fail">
        <Trans>The server certificate could not be read.</Trans>
      </Notice>
    );
  }

  if (certificate.data === undefined) {
    return <Skeleton className="h-64 w-full" />;
  }

  const view = certificate.data;

  return (
    <>
      {view.provisionalUntil !== null ? (
        <ProvisionalNotice key={view.provisionalUntil} until={view.provisionalUntil} />
      ) : null}
      <ServedCertificate view={view} configuration={overview.data?.server ?? null} />
      <ServerNames />
    </>
  );
}
