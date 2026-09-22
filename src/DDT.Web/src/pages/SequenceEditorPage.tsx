// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useQuery } from "@tanstack/react-query";
import { Link, useParams } from "@tanstack/react-router";

import { sequenceQuery } from "@/sequences/sequences";
import { phaseLabel, stepKindLabel } from "@/sequences/steps";

import styles from "./SequenceEditorPage.module.scss";

// One sequence with its steps in order, the phase each runs in and what keeps it from running.
export function SequenceEditorPage() {
  // Not strict, so the page reads its parameters in any router that has its path, as its tests do.
  const sequenceId = useParams({ strict: false }).sequenceId ?? "";
  const sequence = useQuery(sequenceQuery(sequenceId));
  const view = sequence.data ?? null;

  return (
    <div className={styles.page}>
      <Link to="/sequences" className={styles.back}>
        All sequences
      </Link>

      {sequence.isError && <p className={styles.error}>The sequence could not be loaded.</p>}

      {view !== null && (
        <>
          <h1>{view.name}</h1>
          {view.description !== null && <p className={styles.secondary}>{view.description}</p>}

          {view.problems.length > 0 && (
            <ul className={styles.problems}>
              {view.problems.map((problem, index) => (
                <li key={index}>{problem.message}</li>
              ))}
            </ul>
          )}

          <ol className={styles.steps}>
            {view.definition.steps.map((step, index) => {
              const phase = view.stepPhases[index];

              return (
                <li key={step.id}>
                  {`${step.name} (${stepKindLabel(step.kind)}${phase === undefined ? "" : `, ${phaseLabel(phase)}`})`}
                </li>
              );
            })}
          </ol>
        </>
      )}
    </div>
  );
}
