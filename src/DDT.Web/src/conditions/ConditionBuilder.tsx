// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { useId, type ReactNode } from "react";

import type { ConditionChange, ConditionField } from "@/sequences/flow/conditionTree";
import { fieldFindings, type Findings } from "@/sequences/problems";
import type { ConditionNode } from "@/sequences/sequenceConditions";

import { AddKeys } from "./builder/AddKeys";
import { GroupRows } from "./builder/GroupRows";
import { useConditionRows } from "./builder/useConditionRows";
import { conditionSentence, type ConditionUse } from "./conditions";
import type { Subject } from "./conditionSubjects";

const noFindings: Findings = { problems: [], warnings: [] };

export interface ConditionBuilderProps {
  label: ReactNode;
  hint?: ReactNode;
  // Where the condition is used. The sentence under the builder depends on it.
  use: ConditionUse;
  value: ConditionNode | null;
  subjects: readonly Subject[];
  // The member that holds the condition, such as "when". The controls are named after it.
  field?: ConditionField;
  // How a finding names a part, if that isn't field plus the part's path.
  fieldOf?: (path: readonly number[]) => string;
  findings?: Findings;
  onChange: (path: readonly number[], change: ConditionChange) => void;
  isReadOnly?: boolean;
}

// Shows a condition as nested groups of tests, read top to bottom, with the condition as a sentence under them. Each
// control sits in an element named by its place, such as when.parts[1].value, so a finding can move the focus there.
// It doesn't edit anything itself. Each change goes to onChange with the path of the part it changes.
export function ConditionBuilder({
  label,
  hint,
  use,
  value,
  subjects,
  field = "when",
  fieldOf,
  findings = noFindings,
  onChange,
  isReadOnly = false,
}: ConditionBuilderProps) {
  const context = useConditionRows({
    use,
    value,
    subjects,
    field,
    fieldOf,
    findings,
    onChange,
    isReadOnly,
  });
  const labelId = useId();
  const { locked } = context;
  const root = context.place([]);
  const own = fieldFindings(findings, root);

  return (
    <div
      role="group"
      aria-labelledby={labelId}
      data-field={root}
      className="@container flex flex-col gap-1.5"
    >
      <span id={labelId} className="type-label text-ink">
        {label}
      </span>
      {hint ? <span className="type-small text-muted">{hint}</span> : null}
      <div className="mt-1 flex flex-col gap-2.5 rounded-key bg-well p-3 shadow-[inset_0_0_0_1px_var(--color-line-soft)]">
        {value === null ? null : value.kind === "test" ? (
          <GroupRows
            group={{ kind: "all", parts: [value] }}
            path={[]}
            virtual
            depth={1}
            context={context}
          />
        ) : (
          <GroupRows group={value} path={[]} depth={1} context={context} />
        )}
        {value === null && locked ? (
          <p className="type-small text-muted">
            <Trans>No conditions.</Trans>
          </p>
        ) : null}
        {value === null && !locked ? <AddKeys path={[]} depth={1} context={context} /> : null}
      </div>
      <p className="type-small text-muted">{conditionSentence(use, value, subjects)}</p>
      {[...own.problems, ...own.warnings].length > 0 ? (
        <ul className="flex flex-col gap-0.5 type-small">
          {own.problems.map((message) => (
            <li key={message} className="text-fail-text">
              {message}
            </li>
          ))}
          {own.warnings.map((message) => (
            <li key={message} className="text-attention-text">
              {message}
            </li>
          ))}
        </ul>
      ) : null}
    </div>
  );
}
