// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { plural } from "@lingui/core/macro";
import { Trans, useLingui } from "@lingui/react/macro";

import type { TreeIndex } from "../flow/flowTree";
import { sequenceFindings, stepFindings, type Findings } from "../problems";
import { NodeFindingsItem } from "./problems/NodeFindingsItem";
import { SequenceFindingsItem } from "./problems/SequenceFindingsItem";

// Every problem and warning the server found: the sequence's own first, then by node in the order the flow runs. A
// finding takes the focus to its field: it chooses the node, moves the flow to it and opens its fields. Problems keep
// the sequence from running; warnings only tell.
export function ProblemsPanel({
  index,
  findings,
  onGoTo,
}: {
  index: TreeIndex;
  findings: Findings;
  onGoTo: (stepId: string | null, field: string | null) => void;
}) {
  const { t } = useLingui();
  const problemCount = findings.problems.length;
  const warningCount = findings.warnings.length;
  const general = sequenceFindings(
    findings,
    index.entries.map((entry) => entry.node),
  );
  const byNode = index.entries
    .map((entry) => ({ entry, own: stepFindings(findings, entry.node.id) }))
    .filter(({ own }) => own.problems.length + own.warnings.length > 0);

  if (problemCount + warningCount === 0) {
    return (
      <p className="text-ink-2">
        <Trans>None. Nothing keeps this sequence from running.</Trans>
      </p>
    );
  }

  return (
    <div className="flex flex-col gap-4">
      <p className="text-ink-2">
        {problemCount > 0
          ? plural(problemCount, {
              one: "# problem keeps it from running.",
              other: "# problems keep it from running.",
            })
          : null}{" "}
        {warningCount > 0
          ? plural(warningCount, {
              one: "# warning, which does not.",
              other: "# warnings, which do not.",
            })
          : null}
      </p>
      <ul aria-label={t`Problems and warnings`} className="flex flex-col gap-4">
        {general.problems.length + general.warnings.length > 0 ? (
          <SequenceFindingsItem findings={general} onGoTo={onGoTo} />
        ) : null}
        {byNode.map(({ entry, own }) => (
          <NodeFindingsItem
            key={entry.node.id}
            index={index}
            entry={entry}
            own={own}
            onGoTo={onGoTo}
          />
        ))}
      </ul>
    </div>
  );
}
