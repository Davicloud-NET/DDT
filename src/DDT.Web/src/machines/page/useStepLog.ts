// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useId, useState } from "react";

// The machine's log filtered to one step of the run shown, from the step's row or from the flow.
export function useStepLog(runId: string | null) {
  // A step belongs to one run, so its filter ends when another run is shown.
  const [stepFilter, setStepFilter] = useState<{ runId: string | null; stepId: string } | null>(
    null,
  );
  const filteredStep = stepFilter?.runId === runId ? stepFilter.stepId : null;
  const showStepLog = (stepId: string | null) => {
    setStepFilter(stepId === null ? null : { runId, stepId });
  };
  // From the flow, the log is further down the page, so the page goes there.
  const logId = useId();
  const showLogFromFlow = (stepId: string) => {
    showStepLog(stepId);

    const log = document.getElementById(logId);

    if (log !== null) {
      window.scrollTo({ top: log.getBoundingClientRect().top + window.scrollY - 16 });
    }
  };

  return { logId, filteredStep, showStepLog, showLogFromFlow };
}
