// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import type { ReactNode } from "react";

import { Notice } from "@/ui/Notice";

import type { CertificateView } from "../certificate";
import type { ServerSetting } from "../settings";

// Why the page cannot change the certificate, from what configuration says about its files.
export function NotManageable({
  view,
  configuration,
}: {
  view: CertificateView;
  configuration: ServerSetting[] | null;
}) {
  const isSet = (key: string) =>
    configuration?.find((setting) => setting.key === key)?.isSet === true;
  let reason: ReactNode;

  if (configuration === null) {
    reason = view.notManageable;
  } else if (isSet("Kestrel:Certificates:Default:Password")) {
    reason = (
      <Trans>
        Kestrel:Certificates:Default:Password is set in configuration, so the certificate is managed
        by hand and this page only reads it. To manage it here, keep the certificate and its key as
        PEM files without a password, name them in Kestrel:Certificates:Default:Path and KeyPath,
        remove the password, and restart DDT.
      </Trans>
    );
  } else if (
    !isSet("Kestrel:Certificates:Default:Path") ||
    !isSet("Kestrel:Certificates:Default:KeyPath")
  ) {
    reason = (
      <Trans>
        Kestrel:Certificates:Default:Path and KeyPath are not both set in configuration, so DDT has
        no files to keep a certificate in. Name the PEM files there, where DDT may write, and
        restart DDT to manage the certificate here.
      </Trans>
    );
  } else {
    reason = view.notManageable;
  }

  return (
    <Notice tone="attention" title={<Trans>This page cannot change the certificate</Trans>}>
      {reason}
    </Notice>
  );
}
