// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useId, useLayoutEffect, useRef } from "react";

import { stepFindings, type Findings } from "./problems";
import type { SequenceEdit } from "./sequenceEdits";
import type { SequencePhase, SequenceStep } from "./sequences";
import { StepCard } from "./StepCard";
import type { StepCatalog } from "./useSequenceEditor";

import styles from "./StepList.module.scss";

export interface StepListProps {
  steps: SequenceStep[];
  // The phase of each step, in step order.
  phases: SequencePhase[];
  findings: Findings;
  catalog: StepCatalog;
  onEdit: (edit: SequenceEdit) => void;
  onRemove: (stepId: string) => void;
  // Said politely to screen readers, for example where a step moved to.
  onAnnounce: (message: string) => void;
}

interface Group {
  phase: SequencePhase;
  start: number;
  steps: SequenceStep[];
}

function phaseTitle(phase: SequencePhase): string {
  return phase === "WindowsPE" ? "Windows PE" : "Windows, after the hand-over";
}

// The steps in order, divided where the phase changes.
export function StepList({
  steps,
  phases,
  findings,
  catalog,
  onEdit,
  onRemove,
  onAnnounce,
}: StepListProps) {
  const hintId = useId();
  const handles = useRef(new Map<string, HTMLButtonElement>());
  // Where the focus was when a step moved. A step that changes phase is rendered anew, so the focus then goes
  // to its Move button.
  const refocus = useRef<{ element: Element | null; stepId: string } | null>(null);

  useLayoutEffect(() => {
    const pending = refocus.current;

    if (pending === null) {
      return;
    }

    refocus.current = null;

    const { element } = pending;
    const usable =
      element instanceof HTMLElement &&
      element.isConnected &&
      !(element instanceof HTMLButtonElement && element.disabled);

    if (usable) {
      if (document.activeElement !== element) {
        element.focus();
      }
    } else {
      handles.current.get(pending.stepId)?.focus();
    }
  });

  const move = (step: SequenceStep, to: number) => {
    refocus.current = { element: document.activeElement, stepId: step.id };
    onEdit({ type: "moveStep", id: step.id, to });
    onAnnounce(`${step.name} moved to position ${String(to + 1)} of ${String(steps.length)}.`);
  };

  const groups: Group[] = [];

  steps.forEach((step, index) => {
    const phase = phases[index] ?? "WindowsPE";
    const last = groups.at(-1);

    if (last?.phase === phase) {
      last.steps.push(step);
    } else {
      groups.push({ phase, start: index, steps: [step] });
    }
  });

  return (
    <div className={styles.list}>
      {steps.length > 1 && (
        <p id={hintId} className={styles.hint}>
          To move a step with the keyboard, press the arrow keys on its Move button, or Alt with Up
          or Down in its fields.
        </p>
      )}
      {groups.map((group, groupIndex) => (
        <section key={groupIndex} className={styles.group}>
          <h2 className={styles.divider}>{phaseTitle(group.phase)}</h2>
          <ol className={styles.steps} start={group.start + 1}>
            {group.steps.map((step, offset) => {
              const index = group.start + offset;

              return (
                <StepCard
                  key={step.id}
                  step={step}
                  index={index}
                  count={steps.length}
                  phase={group.phase}
                  findings={stepFindings(findings, step.id)}
                  catalog={catalog}
                  moveHintId={hintId}
                  onEdit={onEdit}
                  onMove={(to) => {
                    move(step, to);
                  }}
                  onRemove={() => {
                    onRemove(step.id);
                  }}
                  handleRef={(element) => {
                    if (element === null) {
                      handles.current.delete(step.id);
                    } else {
                      handles.current.set(step.id, element);
                    }
                  }}
                />
              );
            })}
          </ol>
        </section>
      ))}
    </div>
  );
}
