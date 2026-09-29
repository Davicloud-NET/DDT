// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useLingui } from "@lingui/react/macro";

import { findingText, unplacedFindings, type Findings } from "../../problems";
import type { SequenceStep } from "../../sequences";

// The node's findings that no field of the inspector shows.
export function UnplacedFindings({ node, findings }: { node: SequenceStep; findings: Findings }) {
  const { t } = useLingui();
  const unplaced = unplacedFindings(findings, node);

  if (unplaced.problems.length === 0 && unplaced.warnings.length === 0) {
    return null;
  }

  return (
    <ul
      data-findings
      tabIndex={-1}
      aria-label={t`Problems and warnings of this step`}
      className="flex flex-col gap-1 rounded-key bg-well px-3.5 py-2.5 type-small outline-none focus-visible:outline-2 focus-visible:outline-focus"
    >
      {unplaced.problems.map((problem, position) => (
        <li key={`p${String(position)}`} className="text-fail-text">
          {findingText(problem)}
        </li>
      ))}
      {unplaced.warnings.map((warning, position) => (
        <li key={`w${String(position)}`} className="text-attention-text">
          {findingText(warning)}
        </li>
      ))}
    </ul>
  );
}
