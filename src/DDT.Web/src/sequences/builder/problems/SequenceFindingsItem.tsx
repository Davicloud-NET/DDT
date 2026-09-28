// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";

import { findingText, type Findings } from "../../problems";
import { FindingKey } from "./FindingKey";
import { hasField, tonedFindings } from "./problemList";

// The findings of the sequence's own rather than of a node.
export function SequenceFindingsItem({
  findings,
  onGoTo,
}: {
  findings: Findings;
  onGoTo: (stepId: string | null, field: string | null) => void;
}) {
  const { t } = useLingui();

  return (
    <li className="flex flex-col gap-0.5">
      <span className="px-1.5 type-label text-ink">
        <Trans>The sequence</Trans>
      </span>
      {tonedFindings(findings).map(({ finding, tone }, position) => {
        const message = findingText(finding);
        const field = finding.field;

        return (
          <FindingKey
            key={position}
            message={message}
            tone={tone}
            label={t`${message} Go to its field.`}
            onGoTo={
              hasField(field)
                ? () => {
                    onGoTo(null, field);
                  }
                : null
            }
          />
        );
      })}
    </li>
  );
}
