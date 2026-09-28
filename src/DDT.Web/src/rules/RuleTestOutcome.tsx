// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { plural, t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";

import { findingText } from "@/sequences/problems";

import { valueLines, type MachineSequenceResolution, type RuleView } from "./rules";
import { ruleNames, ruleValueSource } from "./ruleText";
import { sequenceLine } from "./ruleTestText";

interface RuleTestOutcomeProps {
  resolution: MachineSequenceResolution;
  rules: readonly RuleView[];
}

// What the rules give the tested machine. That's the matching rules, top first, and the sequence and where it comes
// from. It also shows each value with the rule or machine role it came from, and what else set it.
export function RuleTestOutcome({ resolution, rules }: RuleTestOutcomeProps) {
  const matched = (resolution.matchedRuleIds ?? []).flatMap((id) =>
    rules.filter((rule) => rule.id === id),
  );
  const lines = valueLines(resolution.values ?? []);
  const problems = resolution.valueProblems ?? [];
  const count = resolution.problemCount;

  return (
    <dl className="grid grid-cols-1 gap-y-0.5 type-body sm:grid-cols-[minmax(7rem,11rem)_minmax(0,1fr)] sm:gap-x-4 sm:gap-y-2">
      <dt className="mt-1.5 text-muted first:mt-0 sm:mt-0">
        <Trans>Matches</Trans>
      </dt>
      <dd className="min-w-0">
        {matched.length === 0 ? <Trans>No rule matches this machine.</Trans> : ruleNames(matched)}
      </dd>
      <dt className="mt-1.5 text-muted first:mt-0 sm:mt-0">
        <Trans>Task sequence</Trans>
      </dt>
      <dd className="flex min-w-0 flex-col">
        <span>{sequenceLine(resolution, rules)}</span>
        {count > 0 ? (
          <span className="type-small text-fail-text">
            {plural(count, {
              one: "It has # problem, so it cannot run until it is fixed.",
              other: "It has # problems, so it cannot run until they are fixed.",
            })}
          </span>
        ) : null}
      </dd>
      {lines.map((line) => {
        const source = ruleValueSource(line.used, rules);
        const value = line.used.value;

        return (
          <div key={line.name} className="contents">
            <dt
              className="mt-1.5 min-w-0 truncate pt-px type-data text-ink-2 sm:mt-0"
              title={line.name}
            >
              {line.name}
            </dt>
            <dd className="flex min-w-0 flex-col break-words">
              <span>
                {value === null ? t`A secret, set by ${source}` : t`${value}, from ${source}`}
              </span>
              {line.overridden.map((other, index) => {
                const also = ruleValueSource(other, rules);

                return (
                  <span key={index} className="type-small text-muted">
                    {t`Also set by ${also}, but ${source} comes first.`}
                  </span>
                );
              })}
            </dd>
          </div>
        );
      })}
      {problems.map((problem, index) => (
        <div key={index} className="contents">
          <dt className="mt-1.5 min-w-0 truncate pt-px type-data text-fail-text sm:mt-0">
            {problem.field}
          </dt>
          <dd className="text-fail-text">{findingText(problem)}</dd>
        </div>
      ))}
    </dl>
  );
}
