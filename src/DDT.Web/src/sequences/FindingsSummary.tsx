// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { plural } from "@lingui/core/macro";
import { Trans, useLingui } from "@lingui/react/macro";
import { Button as AriaButton } from "react-aria-components";

import { cx } from "@/ui/cx";
import { Panel } from "@/ui/Layout";

import { findingText, sequenceFindings, stepFindings, type Findings } from "./problems";
import type { SequenceStep } from "./sequences";

// Every problem and warning the server found, the sequence's own first and then by step. A step's finding takes
// the focus to its field. Problems keep the sequence from running; warnings only tell.
export function FindingsSummary({
  steps,
  findings,
  onGoTo,
}: {
  steps: readonly SequenceStep[];
  findings: Findings;
  onGoTo: (stepId: string, field: string | null) => void;
}) {
  const { t } = useLingui();
  const problemCount = findings.problems.length;
  const warningCount = findings.warnings.length;
  const general = sequenceFindings(findings, steps);
  const byStep = steps
    .map((step, index) => ({ step, index, own: stepFindings(findings, step.id) }))
    .filter(({ own }) => own.problems.length + own.warnings.length > 0);

  const toned = (tone: "fail" | "attention") =>
    cx("type-small", tone === "fail" ? "text-fail-text" : "text-attention-text");

  return (
    <Panel title={<Trans>Problems and warnings</Trans>}>
      {problemCount + warningCount === 0 ? (
        <p className="text-ink-2">
          <Trans>None. Nothing keeps this sequence from running.</Trans>
        </p>
      ) : (
        <>
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
          <ul aria-label={t`Problems and warnings`} className="flex flex-col gap-3">
            {general.problems.length + general.warnings.length > 0 ? (
              <li className="flex flex-col gap-1">
                <span className="type-label text-ink">
                  <Trans>The sequence</Trans>
                </span>
                {general.problems.map((finding, index) => (
                  <span key={`p${String(index)}`} className={toned("fail")}>
                    {findingText(finding)}
                  </span>
                ))}
                {general.warnings.map((finding, index) => (
                  <span key={`w${String(index)}`} className={toned("attention")}>
                    {findingText(finding)}
                  </span>
                ))}
              </li>
            ) : null}
            {byStep.map(({ step, index, own }) => {
              const number = String(index + 1).padStart(2, "0");
              const name = step.name;

              return (
                <li key={step.id} className="flex flex-col gap-0.5">
                  <span className="flex items-baseline gap-2 type-label text-ink">
                    <span className="type-numeral text-muted">{number}</span>
                    <span className="truncate">{name}</span>
                  </span>
                  {[
                    ...own.problems.map((finding) => ({ finding, tone: "fail" as const })),
                    ...own.warnings.map((finding) => ({ finding, tone: "attention" as const })),
                  ].map(({ finding, tone }, position) => {
                    const message = findingText(finding);

                    return (
                      <AriaButton
                        key={position}
                        aria-label={t`${message} Go to step ${number}, ${name}.`}
                        onPress={() => {
                          onGoTo(step.id, finding.field);
                        }}
                        className={cx(
                          "-mx-1.5 cursor-pointer rounded-key px-1.5 py-1 text-left outline-none hover:bg-hover",
                          "focus-visible:outline-2 focus-visible:outline-focus",
                          toned(tone),
                        )}
                      >
                        {message}
                      </AriaButton>
                    );
                  })}
                </li>
              );
            })}
          </ul>
        </>
      )}
    </Panel>
  );
}
