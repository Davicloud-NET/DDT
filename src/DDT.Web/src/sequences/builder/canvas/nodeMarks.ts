// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { isWithin, type TreeIndex } from "../../flow/flowTree";
import { stepFindings, type Findings } from "../../problems";

export type FindingMark = "problem" | "warning";

export interface StripItem {
  state: "waiting";
  mark?: FindingMark;
}

// A card's mark for its own findings: a problem outweighs a warning.
export function findingMark(own: Findings): FindingMark | undefined {
  return own.problems.length > 0 ? "problem" : own.warnings.length > 0 ? "warning" : undefined;
}

// The strip of a collapsed container's card, one item for each numbered step inside it.
export function collapsedStrip(index: TreeIndex, findings: Findings, id: string): StripItem[] {
  return index.entries
    .filter(
      (inner) =>
        inner.number !== null && inner.node.id !== id && isWithin(index, inner.node.id, id),
    )
    .map((inner) => {
      const mark = findingMark(stepFindings(findings, inner.node.id));

      return { state: "waiting" as const, ...(mark === undefined ? {} : { mark }) };
    });
}
