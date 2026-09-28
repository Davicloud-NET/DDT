// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { plural } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";

import { formattingLocale } from "@/i18n/i18n";
import { StateTag } from "@/ui/StateTag";

import type { Findings } from "../../problems";
import { findingCounts } from "../../sequenceList";
import type { SequencePhase, SequenceStep } from "../../sequences";
import { phaseLabel, stepKindLabel } from "../../steps";

// Where the node is and how many findings it has, its kind and phase, and how many steps a container holds.
export function NodeSummary({
  node,
  place,
  phases,
  findings,
}: {
  node: SequenceStep;
  place: string;
  phases: readonly SequencePhase[];
  findings: Findings;
}) {
  const kind = stepKindLabel(node.kind);
  const phaseName = new Intl.ListFormat(formattingLocale(), { type: "disjunction" }).format(
    phases.map(phaseLabel),
  );
  const counts = findingCounts(findings.problems.length, findings.warnings.length);
  const thenCount = node.kind === "if" ? node.then.length : 0;
  const elseCount = String(node.kind === "if" ? node.else.length : 0);
  const bodyCount = node.kind === "group" || node.kind === "repeat" ? node.steps.length : 0;

  return (
    <div className="flex flex-col gap-1.5">
      <span className="flex flex-wrap items-center gap-x-3 gap-y-1.5">
        <span className="type-small text-muted">{place}</span>
        {counts !== null ? (
          <StateTag tone={findings.problems.length > 0 ? "fail" : "attention"}>{counts}</StateTag>
        ) : null}
      </span>
      <span className="type-small text-ink-2">
        <Trans>
          {kind}, in {phaseName}.
        </Trans>{" "}
        {node.kind === "if"
          ? plural(thenCount, {
              one: `Then holds # step, Else ${elseCount}.`,
              other: `Then holds # steps, Else ${elseCount}.`,
            })
          : node.kind === "group" || node.kind === "repeat"
            ? plural(bodyCount, { one: "Holds # step.", other: "Holds # steps." })
            : null}
      </span>
    </div>
  );
}
