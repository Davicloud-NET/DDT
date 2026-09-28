// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { SequencePhase, SequenceStep } from "../../sequences";

// The top list's first node that runs only after the hand-over, where the flow draws the line between Windows PE and
// the installed Windows; null where no such line runs across the whole flow.
export function handover(
  steps: readonly SequenceStep[],
  phases: ReadonlyMap<string, readonly SequencePhase[]>,
): string | null {
  const index = steps.findIndex((step) => {
    const own = phases.get(step.id) ?? [];

    return own.length > 0 && !own.includes("WindowsPE");
  });
  const before = steps.slice(0, Math.max(0, index));

  return index > 0 &&
    before.every((step) => (phases.get(step.id) ?? []).every((phase) => phase === "WindowsPE"))
    ? (steps[index]?.id ?? null)
    : null;
}
