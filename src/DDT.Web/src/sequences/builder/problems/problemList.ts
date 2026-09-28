// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { nodeTitle } from "../../flow/flowLabels";
import { ancestorsOf, type TreeIndex } from "../../flow/flowTree";
import { declarationPlace, type Findings } from "../../problems";
import type { SequenceProblem } from "../../sequences";

export type FindingTone = "fail" | "attention";

// Problems first, then warnings.
export function tonedFindings(
  findings: Findings,
): { finding: SequenceProblem; tone: FindingTone }[] {
  return [
    ...findings.problems.map((finding) => ({ finding, tone: "fail" as const })),
    ...findings.warnings.map((finding) => ({ finding, tone: "attention" as const })),
  ];
}

// A finding about the sequence itself only leads to a field if the inspector has one for it.
export function hasField(field: string | null): boolean {
  return field === "name" || field === "description" || declarationPlace(field) !== null;
}

// The titles of the containers a node sits in, the outermost first.
export function trailOf(index: TreeIndex, id: string): string[] {
  return ancestorsOf(index, id)
    .reverse()
    .map((ancestor) => index.byId.get(ancestor)?.node)
    .filter((node) => node !== undefined)
    .map(nodeTitle);
}
