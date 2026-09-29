// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { plural } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";

import { cx } from "@/ui/cx";
import { StateTag } from "@/ui/StateTag";

import type { RuleView } from "../rules";

interface RuleSummaryProps {
  rule: RuleView;
  // The condition as a sentence.
  sentence: string;
  // What the rule does, a few words per effect.
  effects: readonly string[];
  canEdit: boolean;
}

// A rule's name and state, its condition and what it does, in the middle of its row.
export function RuleSummary({ rule, sentence, effects, canEdit }: RuleSummaryProps) {
  const problems = rule.problems.length;

  return (
    <div
      className={cx(
        "row-start-1 flex min-w-0 flex-col gap-1.25",
        canEdit ? "col-start-3" : "col-start-2",
      )}
    >
      <span className="flex min-w-0 items-center gap-2">
        <span className={cx("truncate type-label", rule.enabled ? "text-ink" : "text-ink-2")}>
          {rule.name}
        </span>
        {rule.enabled ? null : (
          <StateTag tone="idle" className="h-5">
            <Trans>Off</Trans>
          </StateTag>
        )}
        {problems > 0 ? (
          <StateTag tone="fail" className="h-5">
            {plural(problems, { one: "# problem", other: "# problems" })}
          </StateTag>
        ) : null}
      </span>
      <span
        className="line-clamp-2 type-small break-words text-muted sm:line-clamp-1"
        title={sentence}
      >
        {sentence}
      </span>
      {effects.length > 0 ? (
        <span className="flex flex-wrap gap-1.5">
          {effects.map((effect, index) => (
            <span
              key={index}
              className="inline-flex h-5.5 max-w-full items-center truncate rounded-tag px-2 type-small whitespace-nowrap text-ink-2 shadow-[inset_0_0_0_1px_var(--color-line)]"
            >
              {effect}
            </span>
          ))}
        </span>
      ) : null}
    </div>
  );
}
