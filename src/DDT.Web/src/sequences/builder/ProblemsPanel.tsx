// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { plural } from "@lingui/core/macro";
import { Trans, useLingui } from "@lingui/react/macro";
import { IconChevronRight } from "@tabler/icons-react";
import { Button as AriaButton } from "react-aria-components";

import { cx } from "@/ui/cx";

import { nodeTitle } from "../flow/flowKeyboard";
import { ancestorsOf, type TreeIndex } from "../flow/flowTree";
import {
  declarationPlace,
  findingText,
  sequenceFindings,
  stepFindings,
  type Findings,
} from "../problems";
import type { SequenceProblem } from "../sequences";

// Every problem and warning the server found: the sequence's own first, then by node in the order the flow runs,
// each under the containers it sits in. A finding takes the focus to its field: it chooses the node, moves the flow to
// it and opens its fields. Problems keep the sequence from running; warnings only tell.
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

  const toned = (tone: "fail" | "attention") =>
    cx("type-small", tone === "fail" ? "text-fail-text" : "text-attention-text");
  const findingKey = (
    finding: SequenceProblem,
    tone: "fail" | "attention",
    position: number,
    goTo: (() => void) | null,
    label: string,
  ) => {
    const message = findingText(finding);

    return goTo === null ? (
      <span key={position} className={cx("px-1.5 py-1", toned(tone))}>
        {message}
      </span>
    ) : (
      <AriaButton
        key={position}
        aria-label={label}
        onPress={goTo}
        className={cx(
          "-mx-0 cursor-pointer rounded-key px-1.5 py-1 text-left motion-colors outline-none hover:bg-hover",
          "focus-visible:outline-2 focus-visible:outline-focus",
          toned(tone),
        )}
      >
        {message}
      </AriaButton>
    );
  };

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
          <li className="flex flex-col gap-0.5">
            <span className="px-1.5 type-label text-ink">
              <Trans>The sequence</Trans>
            </span>
            {[
              ...general.problems.map((finding) => ({ finding, tone: "fail" as const })),
              ...general.warnings.map((finding) => ({ finding, tone: "attention" as const })),
            ].map(({ finding, tone }, position) => {
              const message = findingText(finding);
              const field = finding.field;
              const reachable =
                field === "name" || field === "description" || declarationPlace(field) !== null;

              return findingKey(
                finding,
                tone,
                position,
                reachable
                  ? () => {
                      onGoTo(null, field);
                    }
                  : null,
                t`${message} Go to its field.`,
              );
            })}
          </li>
        ) : null}
        {byNode.map(({ entry, own }) => {
          const title = nodeTitle(entry.node);
          const trail = ancestorsOf(index, entry.node.id)
            .reverse()
            .map((id) => index.byId.get(id)?.node)
            .filter((node) => node !== undefined)
            .map(nodeTitle);

          return (
            <li key={entry.node.id} className="flex flex-col gap-0.5">
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
                  <span className="type-numeral text-muted">
                    {String(entry.number).padStart(2, "0")}
                  </span>
                )}
                <span className="truncate">{title}</span>
              </span>
              {[
                ...own.problems.map((finding) => ({ finding, tone: "fail" as const })),
                ...own.warnings.map((finding) => ({ finding, tone: "attention" as const })),
              ].map(({ finding, tone }, position) => {
                const message = findingText(finding);

                return findingKey(
                  finding,
                  tone,
                  position,
                  () => {
                    onGoTo(entry.node.id, finding.field);
                  },
                  t`${message} Go to ${title}.`,
                );
              })}
            </li>
          );
        })}
      </ul>
    </div>
  );
}
