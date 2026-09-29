// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useLingui } from "@lingui/react/macro";
import { IconChevronRight } from "@tabler/icons-react";

import { nodeTitle } from "../../flow/flowLabels";
import type { TreeEntry, TreeIndex } from "../../flow/flowTree";
import { findingText, type Findings } from "../../problems";
import { FindingKey } from "./FindingKey";
import { tonedFindings, trailOf } from "./problemList";

// A node's findings, under the containers it sits in.
export function NodeFindingsItem({
  index,
  entry,
  own,
  onGoTo,
}: {
  index: TreeIndex;
  entry: TreeEntry;
  own: Findings;
  onGoTo: (stepId: string | null, field: string | null) => void;
}) {
  const { t } = useLingui();
  const title = nodeTitle(entry.node);
  const trail = trailOf(index, entry.node.id);

  return (
    <li className="flex flex-col gap-0.5">
      {trail.length > 0 ? (
        <span className="flex flex-wrap items-center gap-0.5 px-1.5 type-small text-muted">
          {trail.map((part, position) => (
            <span key={position} className="flex items-center gap-0.5">
              {part}
              <IconChevronRight aria-hidden="true" size={12} stroke={2} />
            </span>
          ))}
        </span>
      ) : null}
      <span className="flex items-baseline gap-2 px-1.5 type-label text-ink">
        {entry.number === null ? null : (
          <span className="type-numeral text-muted">{String(entry.number).padStart(2, "0")}</span>
        )}
        <span className="truncate">{title}</span>
      </span>
      {tonedFindings(own).map(({ finding, tone }, position) => {
        const message = findingText(finding);

        return (
          <FindingKey
            key={position}
            message={message}
            tone={tone}
            label={t`${message} Go to ${title}.`}
            onGoTo={() => {
              onGoTo(entry.node.id, finding.field);
            }}
          />
        );
      })}
    </li>
  );
}
