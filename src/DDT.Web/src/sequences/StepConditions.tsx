// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import { IconPlus, IconX } from "@tabler/icons-react";
import { useContext } from "react";
import { Button as AriaButton } from "react-aria-components";

import { Button } from "@/ui/Button";

import { EditorLock } from "./editorLock";
import { ChoiceSetting, TextSetting } from "./fields";
import { fieldFindings, withFieldAt, type Findings } from "./problems";
import type { SequenceEdit } from "./sequenceEdits";
import type { ConditionOperator, SequenceStep } from "./sequences";
import { conditionOperators, machineVariables, operatorLabel, variableLabel } from "./steps";

const row = "grid grid-cols-[minmax(0,11rem)_minmax(0,10rem)_minmax(0,1fr)_2rem] items-start gap-2";

// The step runs only where every condition holds, compared with what the machine reports. Each condition reads as a
// line: the variable, the comparison and the value, with the column names above the first.
export function StepConditions({
  step,
  findings,
  onEdit,
}: {
  step: SequenceStep;
  findings: Findings;
  onEdit: (edit: SequenceEdit) => void;
}) {
  const { t } = useLingui();
  const locked = useContext(EditorLock);
  const list = fieldFindings(findings, "conditions");

  return (
    <section
      aria-labelledby={`conditions-${step.id}`}
      data-field="conditions"
      className="flex flex-col gap-2.5"
    >
      <h3 id={`conditions-${step.id}`} className="type-label text-ink">
        <Trans>Conditions</Trans>
      </h3>
      {step.conditions.length === 0 ? (
        <p className="type-small text-muted">
          <Trans>
            Runs on every machine. Add a condition to run it only where every condition holds.
          </Trans>
        </p>
      ) : (
        <div className="flex flex-col gap-2 overflow-x-auto">
          <div aria-hidden="true" className={`${row} type-small text-muted`}>
            <span>
              <Trans>Variable</Trans>
            </span>
            <span>
              <Trans>Comparison</Trans>
            </span>
            <span>
              <Trans>Value</Trans>
            </span>
          </div>
          {step.conditions.map((condition, index) => {
            const field = `conditions[${String(index)}]`;
            const number = index + 1;
            const variable = condition.variable;
            const known = (machineVariables as readonly string[]).includes(variable);

            return (
              <div key={index} className={row}>
                <ChoiceSetting
                  label={<span className="sr-only">{t`Variable of condition ${number}`}</span>}
                  field={`${field}.variable`}
                  findings={findings}
                  value={variable}
                  choices={[
                    ...(known ? [] : [{ id: variable, label: variable }]),
                    ...machineVariables.map((name) => ({ id: name, label: variableLabel(name) })),
                  ]}
                  onChange={(value) => {
                    onEdit({
                      type: "updateCondition",
                      stepId: step.id,
                      index,
                      patch: { variable: value },
                    });
                  }}
                />
                <ChoiceSetting
                  label={<span className="sr-only">{t`Comparison of condition ${number}`}</span>}
                  field={`${field}.operator`}
                  findings={findings}
                  value={condition.operator}
                  choices={conditionOperators.map((operator) => ({
                    id: operator,
                    label: operatorLabel(operator),
                  }))}
                  onChange={(value) => {
                    onEdit({
                      type: "updateCondition",
                      stepId: step.id,
                      index,
                      patch: { operator: value as ConditionOperator },
                    });
                  }}
                />
                <TextSetting
                  label={<span className="sr-only">{t`Value of condition ${number}`}</span>}
                  field={`${field}.value`}
                  // A finding of the whole condition is shown at its value.
                  findings={withFieldAt(findings, field, `${field}.value`)}
                  {...(variable === "Phase" ? { placeholder: t`WindowsPE or Windows` } : {})}
                  mono={variable !== "Phase" && variable !== "Model" && variable !== "Manufacturer"}
                  value={condition.value}
                  onChange={(value) => {
                    onEdit({ type: "updateCondition", stepId: step.id, index, patch: { value } });
                  }}
                />
                {locked ? null : (
                  <AriaButton
                    aria-label={t`Remove condition ${number}`}
                    onPress={() => {
                      onEdit({ type: "removeCondition", stepId: step.id, index });
                    }}
                    className="mt-1.5 flex size-8 cursor-pointer items-center justify-center rounded-key text-muted key-motion outline-none hover:bg-hover pressed:bg-key-quiet-pressed hover:text-ink focus-visible:outline-2 focus-visible:outline-focus"
                  >
                    <IconX size={16} stroke={2} />
                  </AriaButton>
                )}
              </div>
            );
          })}
        </div>
      )}
      {[...list.problems, ...list.warnings].length > 0 ? (
        <ul className="flex flex-col gap-0.5 type-small">
          {list.problems.map((message) => (
            <li key={message} className="text-fail-text">
              {message}
            </li>
          ))}
          {list.warnings.map((message) => (
            <li key={message} className="text-attention-text">
              {message}
            </li>
          ))}
        </ul>
      ) : null}
      {locked ? null : (
        <div>
          <Button
            size="sm"
            variant="quiet"
            onPress={() => {
              onEdit({ type: "addCondition", stepId: step.id });
            }}
          >
            <IconPlus aria-hidden="true" size={16} stroke={2} />
            <Trans>Add a condition</Trans>
          </Button>
        </div>
      )}
    </section>
  );
}
