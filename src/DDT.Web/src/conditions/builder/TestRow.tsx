// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useLingui } from "@lingui/react/macro";
import { IconX } from "@tabler/icons-react";
import { Button as AriaButton } from "react-aria-components";

import { ChoiceSetting } from "@/sequences/fields/ChoiceSetting";
import { withFieldAt } from "@/sequences/problems";
import type { TestCondition } from "@/sequences/sequenceConditions";
import { operatorTakesValue } from "@/sequences/steps";
import { cx } from "@/ui/cx";

import { operatorsFor, operatorText } from "../conditionOperators";
import { subjectFor } from "../conditionSubjects";
import { valueProblem, withSubject } from "../conditionValues";
import { iconKey } from "./conditionKeys";
import type { RowContext } from "./rowContext";
import { SubjectPicker } from "./SubjectPicker";
import { ValueEditor } from "./ValueEditor";

export function TestRow({
  test,
  path,
  context,
}: {
  test: TestCondition;
  path: readonly number[];
  context: RowContext;
}) {
  const { t } = useLingui();
  const { subjects, place, findings, onChange, remove, locked, numbers } = context;
  const number = numbers.get(path.join(".")) ?? 1;
  const where = place(path);
  const subject = subjectFor(subjects, test.variable);
  const operators = operatorsFor(subject.kind);
  // A finding of the whole test shows at its value.
  const shown = withFieldAt(findings, where, `${where}.value`);
  const early = valueProblem(subject.kind, test.operator, test.value);

  const update = (patch: Partial<Omit<TestCondition, "kind">>) => {
    onChange(path, { op: "update", patch });
  };

  return (
    <div className="flex flex-col gap-1">
      {/* Narrow, as in the inspector, the value takes a line of its own under what is tested and how. */}
      <div className="grid grid-cols-[minmax(0,1fr)_minmax(0,1fr)_1.75rem] items-start gap-1.5 @lg:grid-cols-[minmax(0,10rem)_minmax(0,9rem)_minmax(0,1fr)_1.75rem]">
        <SubjectPicker
          label={t`What condition ${number} tests`}
          field={`${where}.variable`}
          findings={findings}
          subject={subject}
          subjects={subjects}
          locked={locked}
          onChange={(name) => {
            const { variable, operator, value } = withSubject(
              test,
              subject,
              subjectFor(subjects, name),
            );

            update({ variable, operator, value });
          }}
        />
        <ChoiceSetting
          label={<span className="sr-only">{t`Comparison of condition ${number}`}</span>}
          field={`${where}.operator`}
          findings={findings}
          value={test.operator}
          choices={[
            ...(operators.includes(test.operator) ? [] : [test.operator]),
            ...operators,
          ].map((operator) => ({ id: operator, label: operatorText(operator, subject.kind) }))}
          onChange={(operator) => {
            update({ operator: operator as TestCondition["operator"] });
          }}
        />
        {operatorTakesValue(test.operator) ? (
          <div className="col-span-2 row-start-2 @lg:col-span-1 @lg:row-start-auto">
            <ValueEditor
              label={t`Value of condition ${number}`}
              field={`${where}.value`}
              findings={shown}
              subject={subject}
              test={test}
              onChange={(value) => {
                update({ value });
              }}
            />
          </div>
        ) : null}
        {locked ? null : (
          <AriaButton
            aria-label={t`Remove condition ${number}`}
            className={cx(
              iconKey,
              "col-start-3 row-start-1 mt-3 size-7 @lg:col-start-auto @lg:row-start-auto",
            )}
            onPress={() => {
              remove(path);
            }}
          >
            <IconX size={14} stroke={2} />
          </AriaButton>
        )}
      </div>
      {early !== null && !locked ? <p className="type-small text-attention-text">{early}</p> : null}
    </div>
  );
}
