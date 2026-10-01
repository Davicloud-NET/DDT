// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { Notice } from "@/ui/Notice";

import type { BootImageView } from "./bootImage";
import { StaleReasonText } from "./StaleReasonText";

// Whether the served build still fits this server, and each reason why it has to be built again.
export function BuildState({ view }: { view: BootImageView }) {
  if (!view.stale) {
    return view.build === null ? null : (
      <Notice tone="info">
        <Trans>The boot image has every driver flagged for Windows PE.</Trans>
      </Notice>
    );
  }

  return (
    <Notice tone="attention" title={<Trans>The boot image has to be built again</Trans>}>
      <span className="flex flex-col gap-1">
        {view.staleReasons.map((reason) => (
          <span key={reason}>
            <StaleReasonText reason={reason} view={view} />
          </span>
        ))}
      </span>
    </Notice>
  );
}
