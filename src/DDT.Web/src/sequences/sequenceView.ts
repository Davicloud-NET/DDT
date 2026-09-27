// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";

import { formattingLocale } from "@/i18n/i18n";
import type { RailPickerStep } from "@/ui/SequenceRail";

import { stepFindings, type Findings } from "./problems";
import { findingCounts } from "./sequenceList";
import type { SequencePhase, SequenceStep } from "./sequences";
import { stepKindLabel } from "./steps";

// How the editor shows a sequence: the time of a save, the phases over the rail, and a module for each step.

export function clockTime(time: number | string): string {
  return new Date(time).toLocaleTimeString(formattingLocale(), {
    hour: "2-digit",
    minute: "2-digit",
  });
}

// Runs of steps in the same phase, in order, as the rail draws them above its modules.
export function phaseRuns(
  phases: readonly SequencePhase[],
): { phase: SequencePhase; steps: number }[] {
  const runs: { phase: SequencePhase; steps: number }[] = [];

  for (const phase of phases) {
    const last = runs.at(-1);

    if (last?.phase === phase) {
      last.steps++;
    } else {
      runs.push({ phase, steps: 1 });
    }
  }

  return runs;
}

// A module for each step, marked where the server found a problem or a warning, and said in words for screen
// readers, such as "Step 2, Windows 11, Apply image, 1 problem".
export function railSteps(steps: readonly SequenceStep[], findings: Findings): RailPickerStep[] {
  return steps.map((step, index) => {
    const own = stepFindings(findings, step.id);
    const number = index + 1;
    const name = step.name.trim() === "" ? t`Unnamed step` : step.name;
    const kind = stepKindLabel(step.kind);
    const counts = findingCounts(own.problems.length, own.warnings.length);

    return {
      id: step.id,
      state: "waiting",
      name,
      meta: kind,
      ...(own.problems.length > 0
        ? { mark: "problem" as const }
        : own.warnings.length > 0
          ? { mark: "warning" as const }
          : {}),
      label: [t`Step ${number}`, name, name === kind ? null : kind, counts]
        .filter((part) => part !== null)
        .join(", "),
    };
  });
}
