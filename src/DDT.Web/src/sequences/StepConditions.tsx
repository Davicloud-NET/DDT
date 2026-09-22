// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { FormField } from "./FormField";
import { fieldMessages, type Findings } from "./problems";
import type { SequenceEdit } from "./sequenceEdits";
import type { ConditionOperator, SequenceStep } from "./sequences";
import { conditionOperators, machineVariables, operatorLabel, variableLabel } from "./steps";

import formStyles from "./form.module.scss";
import styles from "./StepConditions.module.scss";

export interface StepConditionsProps {
  step: SequenceStep;
  findings: Findings;
  onEdit: (edit: SequenceEdit) => void;
}

// The step runs only where every condition holds, compared with what the machine reports.
export function StepConditions({ step, findings, onEdit }: StepConditionsProps) {
  const listMessages = fieldMessages(findings, "conditions");

  return (
    <fieldset className={styles.conditions}>
      <legend>Conditions</legend>
      {step.conditions.length === 0 && (
        <p className={formStyles.hint}>
          Runs on every machine. Add a condition to run it only where every condition holds.
        </p>
      )}
      {step.conditions.map((condition, index) => {
        const field = `conditions[${String(index)}]`;
        const number = String(index + 1);
        const rowMessages = fieldMessages(findings, field);

        return (
          <div key={index} className={styles.row}>
            <FormField
              label={`Variable of condition ${number}`}
              messages={fieldMessages(findings, `${field}.variable`)}
            >
              {(control) => (
                <select
                  {...control}
                  value={condition.variable}
                  onChange={(event) => {
                    onEdit({
                      type: "updateCondition",
                      stepId: step.id,
                      index,
                      patch: { variable: event.target.value },
                    });
                  }}
                >
                  {!(machineVariables as readonly string[]).includes(condition.variable) && (
                    <option value={condition.variable}>{condition.variable}</option>
                  )}
                  {machineVariables.map((variable) => (
                    <option key={variable} value={variable}>
                      {variableLabel(variable)}
                    </option>
                  ))}
                </select>
              )}
            </FormField>
            <FormField
              label={`Comparison of condition ${number}`}
              messages={fieldMessages(findings, `${field}.operator`)}
            >
              {(control) => (
                <select
                  {...control}
                  value={condition.operator}
                  onChange={(event) => {
                    onEdit({
                      type: "updateCondition",
                      stepId: step.id,
                      index,
                      patch: { operator: event.target.value as ConditionOperator },
                    });
                  }}
                >
                  {conditionOperators.map((operator) => (
                    <option key={operator} value={operator}>
                      {operatorLabel(operator)}
                    </option>
                  ))}
                </select>
              )}
            </FormField>
            <FormField
              label={`Value of condition ${number}`}
              messages={[...rowMessages, ...fieldMessages(findings, `${field}.value`)]}
            >
              {(control) => (
                <input
                  {...control}
                  type="text"
                  value={condition.value}
                  placeholder={condition.variable === "Phase" ? "WindowsPE or Windows" : ""}
                  onChange={(event) => {
                    onEdit({
                      type: "updateCondition",
                      stepId: step.id,
                      index,
                      patch: { value: event.target.value },
                    });
                  }}
                />
              )}
            </FormField>
            <button
              type="button"
              className={styles.remove}
              aria-label={`Remove condition ${number}`}
              onClick={() => {
                onEdit({ type: "removeCondition", stepId: step.id, index });
              }}
            >
              Remove
            </button>
          </div>
        );
      })}
      {listMessages.length > 0 && (
        <ul className={formStyles.messages}>
          {listMessages.map((message) => (
            <li key={message}>{message}</li>
          ))}
        </ul>
      )}
      <div>
        <button
          type="button"
          className={styles.add}
          onClick={() => {
            onEdit({ type: "addCondition", stepId: step.id });
          }}
        >
          Add a condition
        </button>
      </div>
    </fieldset>
  );
}
