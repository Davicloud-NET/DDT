// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { serverText } from "@/lib/serverText";

import type { SequencePhase, SequenceProblem, SequenceStep, StepKind } from "./sequences";

// Problems keep a sequence from running; warnings are shown the same way but do not.
export interface Findings {
  problems: SequenceProblem[];
  warnings: SequenceProblem[];
}

// The fields each kind of step shows, besides name, the flags and the conditions. A problem with another
// field, or none, is shown for the whole step.
const kindFields: Record<StepKind, readonly string[]> = {
  partition: ["systemPartitionMegabytes", "recoveryPartitionMegabytes"],
  applyImage: ["imageId"],
  injectDrivers: ["requireMatch"],
  writeUnattend: ["timeZone", "locale", "keyboard", "localAdministrator"],
  joinDomain: ["organizationalUnit"],
  runScript: [
    "phase",
    "interpreter",
    "script",
    "packageId",
    "timeoutMinutes",
    "successExitCodes",
    "rebootExitCodes",
  ],
  reboot: [],
  writeRawImage: ["imageId"],
  writeCloudInitSeed: ["metaData", "userData", "networkConfig"],
  setVariable: ["variable", "value"],
  pause: ["message", "continueAfterMinutes"],
  group: [],
  if: ["test"],
  repeat: ["until", "maxTimes", "goOnAtLimit"],
};

const commonFields = ["name", "conditions", "continueOnError", "rebootAfter"];

const conditionField = /^conditions\[(\d+)\](?:\.(variable|operator|value))?$/;

// The findings of the whole sequence: no step, or a step the draft no longer has.
export function sequenceFindings(findings: Findings, steps: readonly SequenceStep[]): Findings {
  const ids = new Set(steps.map((step) => step.id));
  const general = (problem: SequenceProblem) => problem.stepId === null || !ids.has(problem.stepId);

  return {
    problems: findings.problems.filter(general),
    warnings: findings.warnings.filter(general),
  };
}

export function stepFindings(findings: Findings, stepId: string): Findings {
  return {
    problems: findings.problems.filter((problem) => problem.stepId === stepId),
    warnings: findings.warnings.filter((problem) => problem.stepId === stepId),
  };
}

// Whether the step's card shows the field, so the finding appears there rather than above the fields.
export function isShownField(step: SequenceStep, field: string | null): boolean {
  if (field === null) {
    return false;
  }

  const condition = conditionField.exec(field);

  if (condition !== null) {
    return Number(condition[1]) < step.conditions.length;
  }

  return commonFields.includes(field) || kindFields[step.kind].includes(field);
}

// Findings of the step that no field shows.
export function unplacedFindings(findings: Findings, step: SequenceStep): Findings {
  const unplaced = (problem: SequenceProblem) => !isShownField(step, problem.field);

  return {
    problems: findings.problems.filter(unplaced),
    warnings: findings.warnings.filter(unplaced),
  };
}

// What a problem or a warning says, in the person's language.
export function findingText(finding: SequenceProblem): string {
  return serverText(finding.code, finding.args, finding.message);
}

// The messages of one field, such as "script" or "conditions[1].value", kept apart: a problem marks the field
// invalid, a warning only tells.
export function fieldFindings(
  findings: Findings,
  field: string,
): { problems: string[]; warnings: string[] } {
  const of = (list: SequenceProblem[]) =>
    list.filter((problem) => problem.field === field).map(findingText);

  return { problems: of(findings.problems), warnings: of(findings.warnings) };
}

// The findings with those of one field shown at another, such as a whole condition's at its value.
export function withFieldAt(findings: Findings, from: string, to: string): Findings {
  const moved = (problem: SequenceProblem) =>
    problem.field === from ? { ...problem, field: to } : problem;

  return { problems: findings.problems.map(moved), warnings: findings.warnings.map(moved) };
}

// The phase each step runs in, as the server worked it out for the copy it holds. A step the server has not
// seen yet, being new, runs in the phase of the step before it until the next save says otherwise.
export function phasesOf(
  steps: readonly SequenceStep[],
  saved: readonly SequenceStep[],
  savedPhases: readonly SequencePhase[],
): SequencePhase[] {
  const known = new Map<string, SequencePhase>();

  saved.forEach((step, index) => {
    const phase = savedPhases[index];

    if (phase !== undefined) {
      known.set(step.id, phase);
    }
  });

  const phases: SequencePhase[] = [];

  for (const step of steps) {
    phases.push(known.get(step.id) ?? phases.at(-1) ?? "WindowsPE");
  }

  return phases;
}
