// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

// The line between WinPE and the installed Windows. It's drawn above the node whose top edge is at top.
export function HandoverLine({ top, width }: { top: number; width: number }) {
  return (
    <div aria-hidden="true">
      <div
        className="absolute border-t-[1.5px] border-dashed border-control"
        style={{ left: -48, width: width + 96, top: top - 22 }}
      />
      <span
        className="absolute type-small whitespace-nowrap text-muted"
        style={{ left: -44, top: top - 44 }}
      >
        <Trans>In Windows PE</Trans>
      </span>
      <span
        className="absolute type-small whitespace-nowrap text-muted"
        style={{ left: -44, top: top - 16 }}
      >
        <Trans>In the installed Windows, after the hand-over</Trans>
      </span>
    </div>
  );
}
