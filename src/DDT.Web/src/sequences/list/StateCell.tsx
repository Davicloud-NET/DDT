// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { StateTag } from "@/ui/StateTag";

import { findingCounts } from "../sequenceList";
import type { SequenceSummary } from "../sequences";

// Whether the sequence can run. A sequence with problems is a draft; warnings only tell.
export function StateCell({ sequence }: { sequence: SequenceSummary }) {
  const counts = findingCounts(sequence.problemCount, sequence.warningCount);

  return (
    <span className="flex flex-col items-start gap-1">
      <StateTag tone={sequence.problemCount > 0 ? "fail" : "ok"}>
        {sequence.problemCount > 0 ? <Trans>Cannot run</Trans> : <Trans>Ready to run</Trans>}
      </StateTag>
      {counts !== null ? <span className="type-small text-muted">{counts}</span> : null}
    </span>
  );
}
