// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useId, useState } from "react";

import type { StepKind } from "./sequences";
import { stepKindLabel, stepKinds } from "./steps";

import styles from "./AddStep.module.scss";

export interface AddStepProps {
  // The button, such as "Add step" or "Insert after".
  action: string;
  // Names the place, such as "Kind of step to insert after Apply image".
  kindLabel: string;
  onAdd: (kind: StepKind) => void;
}

export function AddStep({ action, kindLabel, onAdd }: AddStepProps) {
  const id = useId();
  const [kind, setKind] = useState<StepKind>("runScript");

  return (
    <div className={styles.add}>
      <label htmlFor={id} className={styles.label}>
        {kindLabel}
      </label>
      <select
        id={id}
        value={kind}
        onChange={(event) => {
          setKind(event.target.value as StepKind);
        }}
      >
        {stepKinds.map((choice) => (
          <option key={choice} value={choice}>
            {stepKindLabel(choice)}
          </option>
        ))}
      </select>
      <button
        type="button"
        className={styles.button}
        onClick={() => {
          onAdd(kind);
        }}
      >
        {action}
      </button>
    </div>
  );
}
