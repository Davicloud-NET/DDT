// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useEffect, useEffectEvent } from "react";

import type { SequenceStep } from "./sequences";
import { stepKindLabel } from "./steps";

import styles from "./RemovedStepNotice.module.scss";

// How long a removed step can be brought back from here.
const UNDO_MS = 10_000;

export interface RemovedStepNoticeProps {
  step: SequenceStep;
  onUndo: () => void;
  onDismiss: () => void;
}

// Removing a step asks nothing; this brings it back for a few seconds. The parent keys it by the step, so
// each removal gets its own time. Focus comes here, as the removed step had it.
export function RemovedStepNotice({ step, onUndo, onDismiss }: RemovedStepNoticeProps) {
  const dismiss = useEffectEvent(() => {
    onDismiss();
  });

  useEffect(() => {
    const timer = window.setTimeout(() => {
      dismiss();
    }, UNDO_MS);

    return () => {
      window.clearTimeout(timer);
    };
  }, []);

  return (
    <p className={styles.notice} role="status">
      {`Removed ${stepKindLabel(step.kind)}: ${step.name}. `}
      <button type="button" className={styles.undo} autoFocus onClick={onUndo}>
        Undo
      </button>
    </p>
  );
}
