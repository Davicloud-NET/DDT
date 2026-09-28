// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { i18n, type MessageDescriptor } from "@lingui/core";
import { msg, t } from "@lingui/core/macro";

import type {
  DeploymentState,
  DeploymentStepView,
  DeploymentSummary,
  DeploymentView,
} from "@/deployments/deployments";
import { formatDuration } from "@/lib/format";
import { formatMac, type MachineSummary } from "@/machines/machines";
import { walk } from "@/sequences/flow/flowTree";
import type { ConditionOperator, StepCondition } from "@/sequences/sequenceConditions";
import type { SequenceDefinition, SequencePhase, SequenceStep } from "@/sequences/sequences";
import { operatorTakesValue, phaseLabel, variableLabel } from "@/sequences/steps";

import { leafNumbers } from "./runPath";

// One moment of a run, on the server's clock.
export interface TimelineEntry {
  key: string;
  utc: string;
  text: string;
}

const operatorLabels: Record<ConditionOperator, MessageDescriptor> = {
  Equals: msg`is`,
  NotEquals: msg`is not`,
  StartsWith: msg`starts with`,
  Contains: msg`contains`,
  NotContains: msg`does not contain`,
  EndsWith: msg`ends with`,
  Matches: msg`matches`,
  In: msg`is one of`,
  Exists: msg`has a value`,
  NotExists: msg`has no value`,
  Greater: msg`is greater than`,
  GreaterOrEqual: msg`is at least`,
  Less: msg`is less than`,
  LessOrEqual: msg`is at most`,
  InSubnet: msg`is in the network`,
};

export function describeCondition(condition: StepCondition): string {
  const variable = variableLabel(condition.variable);
  const operator = i18n._(operatorLabels[condition.operator]);
  const value = condition.value;

  return operatorTakesValue(condition.operator)
    ? t`${variable} ${operator} "${value}"`
    : t`${variable} ${operator}`;
}

// What the machine reports for a condition's variable now; the run checked what it reported then.
function reported(variable: string, machine: MachineSummary, phase: SequencePhase): string | null {
  switch (variable) {
    case "Manufacturer":
      return machine.manufacturer;
    case "Model":
      return machine.model;
    case "SerialNumber":
      return machine.serialNumber;
    case "SmbiosUuid":
      return machine.smbiosUuid;
    case "MacAddress":
      return machine.macAddresses.map(formatMac).join(", ");
    case "ComputerName":
      return machine.assignedName;
    case "Phase":
      return phaseLabel(phase);
    default:
      return null;
  }
}

// Why a step was skipped, for an agent that recorded no tests, as agents before version 3 sequences do not; the others
// get decisionLine. The engine skips only when a condition does not hold, so each shows beside what the machine
// reports now.
export function skipReason(
  step: DeploymentStepView,
  planned: SequenceStep | undefined,
  machine: MachineSummary | null,
): string {
  if (step.error !== null) {
    return step.error;
  }

  if (planned === undefined || planned.conditions.length === 0) {
    return t`Skipped.`;
  }

  const conditions = planned.conditions
    .map((condition) => {
      const value = machine === null ? null : reported(condition.variable, machine, step.phase);
      const described = describeCondition(condition);

      return value === null ? described : t`${described}, and the machine reports "${value}"`;
    })
    .join("; ");

  return t`Skipped, because not every condition held: ${conditions}.`;
}

// Every node of the run's tree by its id, containers and what is inside them included.
export function plannedSteps(definition: SequenceDefinition | null): Map<string, SequenceStep> {
  return new Map(walk(definition?.steps ?? []).map((entry) => [entry.node.id, entry.node]));
}

// Whether the run went on after a failed step. Stopping or rejecting the run also marks its running step
// failed, and then the run ended there although the step goes on when it fails.
export function wentOnAfter(
  step: DeploymentStepView,
  steps: readonly DeploymentStepView[],
  run: DeploymentState,
): boolean {
  return (
    run === "Running" ||
    run === "Done" ||
    steps.some((other) => other.index > step.index && other.state !== "Pending")
  );
}

// A started step's time: until it ended, or until now while it runs.
export function stepDuration(step: DeploymentStepView, now: number): number | null {
  if (step.startedUtc === null) {
    return null;
  }

  const end = step.finishedUtc === null ? now : Date.parse(step.finishedUtc);

  return end - Date.parse(step.startedUtc);
}

// How far the whole run is, from 0 to 100: every finished or skipped step counts whole, the running one by its
// percentage. Steps take very different times, so this is a rough measure, shown as such.
export function runPercent(steps: readonly DeploymentStepView[]): number {
  if (steps.length === 0) {
    return 0;
  }

  const done = steps.reduce(
    (sum, step) =>
      sum +
      (step.state === "Running"
        ? Math.min(100, Math.max(0, step.percent)) / 100
        : step.state === "Pending"
          ? 0
          : 1),
    0,
  );

  return Math.round((done / steps.length) * 100);
}

function assignment(run: DeploymentSummary): string {
  const by = run.requestedBy ?? t`an operator`;
  const title = run.title;

  switch (run.source) {
    case "Web":
      return t`${by} assigned ${title} on the web`;
    case "Rule":
      return t`${by} approved the machine on the web to run ${title}, which a rule chose`;
    case "Console":
      return t`${by} signed in at the machine and chose ${title} there`;
  }
}

function gap(from: string, to: string): string {
  return formatDuration(Date.parse(to) - Date.parse(from));
}

// From what the server records: the machine's first registration, the assignment or approval, the start, each
// restart and the hand-over to Windows as the step times show them, and the end.
export function runTimeline(
  machine: MachineSummary | null,
  run: DeploymentSummary,
  steps: readonly DeploymentStepView[],
  definition: SequenceDefinition | null,
): TimelineEntry[] {
  const planned = plannedSteps(definition);
  const numbers = leafNumbers(steps);
  const entries: TimelineEntry[] = [];

  if (machine !== null && Date.parse(machine.firstSeenUtc) <= Date.parse(run.createdUtc)) {
    const from = machine.firstSeenAddress;

    entries.push({
      key: "registered",
      utc: machine.firstSeenUtc,
      text:
        from === null
          ? t`The machine registered for the first time`
          : t`The machine registered for the first time, from ${from}`,
    });
  }

  entries.push({ key: "assigned", utc: run.createdUtc, text: assignment(run) });

  if (run.startedUtc !== null) {
    entries.push({ key: "started", utc: run.startedUtc, text: t`The agent started the run` });
  }

  // Containers neither restart nor hand over; the leaves in them do.
  const leaves = [...steps]
    .sort((a, b) => a.index - b.index)
    .filter((step) => numbers.get(step.stepId) !== null);

  leaves.forEach((step, position) => {
    if (step.finishedUtc === null || step.state !== "Done") {
      return;
    }

    // Skipped steps never start, so the machine was back when the next started step did.
    const next = leaves.slice(position + 1).find((later) => later.startedUtc !== null) ?? null;
    const back = next?.startedUtc ?? null;
    const handsOver = step.phase === "WindowsPE" && leaves[position + 1]?.phase === "Windows";
    const restarts = step.kind === "reboot" || planned.get(step.stepId)?.rebootAfter === true;
    const number = numbers.get(step.stepId) ?? step.index + 1;
    const name = step.name;
    const took = back === null ? "" : gap(step.finishedUtc, back);

    if (handsOver) {
      entries.push({
        key: `handover-${step.stepId}`,
        utc: step.finishedUtc,
        text:
          back === null || next?.phase !== "Windows"
            ? t`The hand-over to Windows began`
            : t`Handed over to Windows; after Windows setup the agent continued there ${took} later`,
      });
    } else if (restarts) {
      entries.push({
        key: `restart-${step.stepId}`,
        utc: step.finishedUtc,
        text:
          back === null
            ? t`Restarted after step ${number}, ${name}`
            : t`Restarted after step ${number}, ${name}; back after ${took}`,
      });
    }
  });

  if (run.finishedUtc !== null) {
    const error = run.error;

    entries.push({
      key: "finished",
      utc: run.finishedUtc,
      text:
        run.state === "Done"
          ? t`The run is done`
          : run.state === "Cancelled"
            ? t`The run was cancelled`
            : error === null
              ? t`The run failed`
              : t`The run failed: ${error}`,
    });
  }

  return entries.sort((a, b) => Date.parse(a.utc) - Date.parse(b.utc));
}

// Says that whoever started the run let it write a raw disk image that may not start with Secure Boot on; null
// otherwise.
export function secureBootAllowance(view: DeploymentView): string | null {
  if (!view.allowSecureBootMismatch) {
    return null;
  }

  const step = [...plannedSteps(view.definition).values()].find(
    (candidate) => candidate.kind === "writeRawImage",
  );
  const image = view.artifacts.find((artifact) => artifact.stepId === step?.id)?.name;

  return image === undefined
    ? t`Allowed to write its raw disk image although it may not start with Secure Boot on.`
    : t`Allowed to write ${image} although it may not start with Secure Boot on.`;
}
