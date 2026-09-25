// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { KeyboardEvent } from "react";

import { AddStep } from "./AddStep";
import { FindingList } from "./FindingList";
import { FormCheckbox } from "./FormCheckbox";
import { FormField } from "./FormField";
import { fieldMessages, unplacedFindings, type Findings } from "./problems";
import { insertStepAfter, type SequenceEdit } from "./sequenceEdits";
import type { SequencePhase, SequenceStep } from "./sequences";
import { StepConditions } from "./StepConditions";
import { StepFields } from "./steps/StepFields";
import { phaseLabel, stepKindLabel } from "./steps";
import type { StepCatalog } from "./useSequenceEditor";

import styles from "./StepCard.module.scss";

export interface StepCardProps {
  step: SequenceStep;
  index: number;
  count: number;
  phase: SequencePhase;
  // This step's findings.
  findings: Findings;
  catalog: StepCatalog;
  // Explains how the keyboard moves a step.
  moveHintId: string;
  onEdit: (edit: SequenceEdit) => void;
  onMove: (to: number) => void;
  onRemove: () => void;
  handleRef: (element: HTMLButtonElement | null) => void;
}

// Alt with Up or Down moves the step from its fields too, except where the keys choose within the field.
function movesFrom(target: EventTarget): boolean {
  return !(target instanceof HTMLTextAreaElement || target instanceof HTMLSelectElement);
}

export function StepCard({
  step,
  index,
  count,
  phase,
  findings,
  catalog,
  moveHintId,
  onEdit,
  onMove,
  onRemove,
  handleRef,
}: StepCardProps) {
  const moveTo = (to: number) => {
    if (to >= 0 && to < count && to !== index) {
      onMove(to);
    }
  };

  const onHandleKey = (event: KeyboardEvent<HTMLButtonElement>) => {
    const targets: Record<string, number> = {
      ArrowUp: index - 1,
      ArrowDown: index + 1,
      Home: 0,
      End: count - 1,
    };
    const to = targets[event.key];

    if (!event.altKey && to !== undefined) {
      event.preventDefault();
      moveTo(to);
    }
  };

  const onCardKey = (event: KeyboardEvent<HTMLLIElement>) => {
    if (
      event.altKey &&
      (event.key === "ArrowUp" || event.key === "ArrowDown") &&
      movesFrom(event.target)
    ) {
      event.preventDefault();
      moveTo(event.key === "ArrowUp" ? index - 1 : index + 1);
    }
  };

  const badges = [
    ...(step.rebootAfter ? ["Restarts after it"] : []),
    ...(step.continueOnError ? ["Goes on when it fails"] : []),
    ...(step.conditions.length > 0 ? ["Runs only where its conditions hold"] : []),
  ];

  return (
    <li className={styles.card} data-step-id={step.id} onKeyDown={onCardKey}>
      <div className={styles.header}>
        <button
          ref={handleRef}
          type="button"
          className={styles.handle}
          aria-label={`Move ${step.name}`}
          aria-describedby={moveHintId}
          onKeyDown={onHandleKey}
        >
          Move
        </button>
        <h3 className={styles.position}>{`Step ${String(index + 1)} of ${String(count)}`}</h3>
        <span className={styles.facts}>{`${stepKindLabel(step.kind)}, ${phaseLabel(phase)}`}</span>
        {badges.map((badge) => (
          <span key={badge} className={styles.badge}>
            {badge}
          </span>
        ))}
      </div>

      <FindingList findings={unplacedFindings(findings, step)} />

      <FormField label="Name" messages={fieldMessages(findings, "name")}>
        {(control) => (
          <input
            {...control}
            type="text"
            value={step.name}
            onChange={(event) => {
              onEdit({ type: "updateStep", id: step.id, patch: { name: event.target.value } });
            }}
          />
        )}
      </FormField>

      <StepFields
        step={step}
        findings={findings}
        catalog={catalog}
        onChange={(patch, chosen) => {
          onEdit({
            type: "updateStep",
            id: step.id,
            patch,
            ...(chosen === true ? { chosen } : {}),
          });
        }}
      />

      <FormCheckbox
        label="Go on when this step fails"
        checked={step.continueOnError}
        messages={fieldMessages(findings, "continueOnError")}
        onChange={(continueOnError) => {
          onEdit({ type: "updateStep", id: step.id, patch: { continueOnError } });
        }}
      />
      {(step.kind !== "reboot" || step.rebootAfter) && (
        <FormCheckbox
          label="Restart after this step"
          checked={step.rebootAfter}
          messages={fieldMessages(findings, "rebootAfter")}
          onChange={(rebootAfter) => {
            onEdit({ type: "updateStep", id: step.id, patch: { rebootAfter } });
          }}
        />
      )}

      <StepConditions step={step} findings={findings} onEdit={onEdit} />

      <div className={styles.actions}>
        <button
          type="button"
          className={styles.button}
          aria-label={`Move ${step.name} up`}
          disabled={index === 0}
          onClick={() => {
            moveTo(index - 1);
          }}
        >
          Move up
        </button>
        <button
          type="button"
          className={styles.button}
          aria-label={`Move ${step.name} down`}
          disabled={index === count - 1}
          onClick={() => {
            moveTo(index + 1);
          }}
        >
          Move down
        </button>
        <button
          type="button"
          className={styles.button}
          aria-label={`Remove ${step.name}`}
          onClick={onRemove}
        >
          Remove
        </button>
        <AddStep
          action="Insert"
          kindLabel={`Step to insert after ${step.name}`}
          onAdd={(kind) => {
            onEdit(insertStepAfter(step.id, kind));
          }}
        />
      </div>
    </li>
  );
}
