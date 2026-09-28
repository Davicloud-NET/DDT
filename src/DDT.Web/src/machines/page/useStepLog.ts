// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useId, useState } from "react";

// Filters the machine's log to one step of the shown run. The step is picked from its row or from the flow.
export function useStepLog(runId: string | null) {
  // A step belongs to one run, so its filter ends when another run is shown.
  const [stepFilter, setStepFilter] = useState<{ runId: string | null; stepId: string } | null>(
    null,
  );
  const filteredStep = stepFilter?.runId === runId ? stepFilter.stepId : null;
  const showStepLog = (stepId: string | null) => {
    setStepFilter(stepId === null ? null : { runId, stepId });
  };
  // The log is further down the page than the flow, so the page scrolls to it.
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
