// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { installedPhase, isBootPhase, sequenceKind } from "../../sequenceKinds";
import type { SequencePhase, SequenceStep } from "../../sequences";

// The first top-level node that runs only in the installed system of the sequence's kind, after nodes that run only
// in a boot phase. The flow draws the hand-over line there. Null if no such line runs across the whole flow.
export function handover(
  steps: readonly SequenceStep[],
  phases: ReadonlyMap<string, readonly SequencePhase[]>,
): string | null {
  const installed = installedPhase(sequenceKind(steps));
  const index = steps.findIndex((step) => {
    const own = phases.get(step.id) ?? [];

    return own.length > 0 && own.every((phase) => phase === installed);
  });
  const before = steps.slice(0, Math.max(0, index));

  return index > 0 && before.every((step) => (phases.get(step.id) ?? []).every(isBootPhase))
    ? (steps[index]?.id ?? null)
    : null;
}
