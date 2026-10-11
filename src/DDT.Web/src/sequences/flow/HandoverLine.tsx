// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import type { SequenceKind } from "../sequences";

interface HandoverLineProps {
  // The top edge of the node the line is drawn above.
  top: number;
  width: number;
  kind: SequenceKind;
}

// The line between a sequence's boot environment and the installed system it goes on in.
export function HandoverLine({ top, width, kind }: HandoverLineProps) {
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
        {kind === "Linux" ? (
          <Trans>In the Linux boot environment</Trans>
        ) : (
          <Trans>In Windows PE</Trans>
        )}
      </span>
      <span
        className="absolute type-small whitespace-nowrap text-muted"
        style={{ left: -44, top: top - 16 }}
      >
        {kind === "Linux" ? (
          <Trans>In the installed Linux, after the hand-over</Trans>
        ) : (
          <Trans>In the installed Windows, after the hand-over</Trans>
        )}
      </span>
    </div>
  );
}
