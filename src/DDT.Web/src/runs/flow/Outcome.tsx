// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { IconCheck, IconX } from "@tabler/icons-react";

import type { Subject } from "@/conditions/conditionSubjects";

import { reportedText, testWords, type TestOutcome } from "../decisions";

// One test of a decision: the test as a sentence, whether it held, and the value it met.
export function Outcome({
  outcome,
  subjects,
}: {
  outcome: TestOutcome;
  subjects: readonly Subject[];
}) {
  const words = outcome.test === null ? null : testWords(outcome.test, subjects);
  const subject = words?.subject ?? outcome.path;
  const operator = words?.operator ?? "";
  const value = words?.value ?? null;

  return (
    <div className="flex flex-col gap-1">
      <div className="flex items-start justify-between gap-3 type-body">
        <span className="min-w-0 text-ink">
          {value === null ? (
            <Trans>
              <span className="font-semibold text-ink-2">{subject}</span>{" "}
              <span className="text-muted">{operator}</span>
            </Trans>
          ) : (
            <Trans>
              <span className="font-semibold text-ink-2">{subject}</span>{" "}
              <span className="text-muted">{operator}</span> {value}
            </Trans>
          )}
        </span>
        {outcome.held ? (
          <span className="flex shrink-0 items-center gap-1 font-semibold text-ok-text">
            <IconCheck aria-hidden="true" size={14} stroke={2.25} />
            <Trans>Holds</Trans>
          </span>
        ) : (
          <span className="flex shrink-0 items-center gap-1 font-semibold text-ink-2">
            <IconX aria-hidden="true" size={14} stroke={2.25} />
            <Trans>Does not hold</Trans>
          </span>
        )}
      </div>
      <span className="type-small text-muted">{reportedText(outcome, subjects)}</span>
    </div>
  );
}
