// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { serverText } from "@/lib/serverText";

import { conditionOf, type ConditionField } from "./flow/conditionTree";
import { walk } from "./flow/flowTree";
import type {
  NodePhase,
  SequencePhase,
  SequenceProblem,
  SequenceStep,
  StepKind,
} from "./sequences";

// Problems keep a sequence from running; warnings are shown the same way but do not.
export interface Findings {
  problems: SequenceProblem[];
  warnings: SequenceProblem[];
}

// The fields each kind of node shows, besides name, the flags, its conditions and its shares. A problem with another
// field, or none, is shown for the whole node.
const kindFields: Record<StepKind, readonly string[]> = {
  partition: ["systemPartitionMegabytes", "recoveryPartitionMegabytes"],
  applyImage: ["imageId"],
  injectDrivers: ["requireMatch"],
  writeUnattend: ["timeZone", "locale", "keyboard", "localAdministrator"],
  joinDomain: ["organizationalUnit", "account"],
  runScript: [
    "phase",
    "interpreter",
    "script",
    "packageId",
    "timeoutMinutes",
    "successExitCodes",
    "rebootExitCodes",
    "runAs",
  ],
  reboot: [],
  writeRawImage: ["imageId"],
  writeCloudInitSeed: ["metaData", "userData", "networkConfig"],
  setVariable: ["variable", "value"],
  pause: ["message", "continueAfterMinutes"],
  group: [],
  if: [],
  repeat: ["maxTimes", "goOnAtLimit"],
};

const commonFields = ["name", "continueOnError", "rebootAfter"];

// One part of a finding's field, such as parts[1] in when.parts[1].value: a member, and its index where it is a list.
export interface FieldSegment {
  name: string;
  index: number | null;
}

// A finding's field as its parts, such as "when.parts[1].value", "variables[2].name" or "shares[0].path"; null for
// none or for one written some other way.
export function parseFieldPath(field: string | null): FieldSegment[] | null {
  if (field === null || field === "") {
    return null;
  }

  const segments: FieldSegment[] = [];

  for (const part of field.split(".")) {
    const match = /^([A-Za-z][A-Za-z0-9]*)(?:\[(\d+)\])?$/.exec(part);

    if (match === null) {
      return null;
    }

    segments.push({
      name: match[1] ?? "",
      index: match[2] === undefined ? null : Number(match[2]),
    });
  }

  return segments;
}

// Where in a condition a field is: the condition, the path of parts to it, and the member of a test, such as
// { condition: "when", path: [1], member: "value" } for when.parts[1].value. A legacy condition is conditions[i].
export interface ConditionPlace {
  condition: ConditionField | "conditions";
  path: number[];
  member: "variable" | "operator" | "value" | null;
}

export function conditionPlace(field: string | null): ConditionPlace | null {
  const segments = parseFieldPath(field);
  const [first, ...rest] = segments ?? [];

  if (first === undefined) {
    return null;
  }

  if (first.name === "conditions") {
    const [member] = rest;

    return {
      condition: "conditions",
      path: first.index === null ? [] : [first.index],
      member:
        member?.name === "variable" || member?.name === "operator" || member?.name === "value"
          ? member.name
          : null,
    };
  }

  if (
    first.index !== null ||
    (first.name !== "when" && first.name !== "test" && first.name !== "until")
  ) {
    return null;
  }

  const path: number[] = [];
  let member: ConditionPlace["member"] = null;

  for (const segment of rest) {
    if (segment.name === "parts" && segment.index !== null && member === null) {
      path.push(segment.index);
    } else if (
      segment.index === null &&
      member === null &&
      (segment.name === "variable" || segment.name === "operator" || segment.name === "value")
    ) {
      member = segment.name;
    } else {
      return null;
    }
  }

  return { condition: first.name, path, member };
}

// A field of the sequence's own declarations, such as variables[2].name or inputs[0].choices[1].value.
export interface DeclarationPlace {
  list: "variables" | "inputs";
  index: number;
  member: string | null;
}

export function declarationPlace(field: string | null): DeclarationPlace | null {
  const [first, second] = parseFieldPath(field) ?? [];

  if (first?.index == null || (first.name !== "variables" && first.name !== "inputs")) {
    return null;
  }

  return { list: first.name, index: first.index, member: second?.name ?? null };
}

// The findings of the whole sequence: no node, or a node the draft no longer has.
export function sequenceFindings(findings: Findings, steps: readonly SequenceStep[]): Findings {
  const ids = new Set(walk(steps).map((entry) => entry.node.id));
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

// Whether the node's inspector shows the field, so the finding appears there rather than above the fields.
export function isShownField(step: SequenceStep, field: string | null): boolean {
  const segments = parseFieldPath(field);
  const [first, second] = segments ?? [];

  if (first === undefined || field === null) {
    return false;
  }

  const condition = conditionPlace(field);

  if (condition !== null) {
    switch (condition.condition) {
      case "conditions":
        return (condition.path[0] ?? 0) < step.conditions.length && step.kind !== "if";
      case "when":
        return step.kind !== "if";
      default:
        return conditionOf(step, condition.condition) !== undefined;
    }
  }

  if (first.name === "shares") {
    return (
      first.index !== null &&
      first.index < (step.shares ?? []).length &&
      (second === undefined || second.name === "path" || second.name === "account")
    );
  }

  return (
    segments?.length === 1 &&
    first.index === null &&
    (commonFields.includes(first.name) || kindFields[step.kind].includes(first.name))
  );
}

// Findings of the node that no field shows.
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

// The messages of one field, such as "script" or "when.parts[1].value", kept apart: a problem marks the field
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

// The phases each node may run in, as the server worked them out for the copy it holds: from its nodePhases, or from
// stepPhases for the steps at the top where it sends none. A node the server has not seen yet, being new, runs in the
// phase of the node before it in the walk until the next save says otherwise.
export function nodePhasesOf(
  steps: readonly SequenceStep[],
  saved: {
    steps: readonly SequenceStep[];
    stepPhases: readonly SequencePhase[];
    nodePhases?: readonly NodePhase[] | null | undefined;
  },
): Map<string, SequencePhase[]> {
  const known = new Map<string, SequencePhase[]>();

  if (saved.nodePhases !== null && saved.nodePhases !== undefined) {
    for (const node of saved.nodePhases) {
      known.set(node.nodeId, node.phases);
    }
  } else {
    saved.steps.forEach((step, index) => {
      const phase = saved.stepPhases[index];

      if (phase !== undefined) {
        known.set(step.id, [phase]);
      }
    });
  }

  const phases = new Map<string, SequencePhase[]>();
  let previous: SequencePhase[] = ["WindowsPE"];

  for (const { node } of walk(steps)) {
    const own = known.get(node.id) ?? previous.slice(-1);

    phases.set(node.id, own);
    previous = own;
  }

  return phases;
}
