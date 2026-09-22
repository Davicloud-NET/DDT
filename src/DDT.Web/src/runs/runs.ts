// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type {
  DeploymentState,
  DeploymentStepView,
  DeploymentSummary,
} from "@/deployments/deployments";
import { formatDuration } from "@/lib/format";
import { formatMac, type MachineSummary } from "@/machines/machines";
import type {
  ConditionOperator,
  SequenceDefinition,
  SequencePhase,
  SequenceStep,
  StepCondition,
} from "@/sequences/sequences";
import { phaseLabel } from "@/sequences/steps";

// One moment of a run, on the server's clock.
export interface TimelineEntry {
  key: string;
  utc: string;
  text: string;
}

const variableLabels = new Map([
  ["Manufacturer", "Manufacturer"],
  ["Model", "Model"],
  ["SerialNumber", "Serial number"],
  ["SmbiosUuid", "SMBIOS UUID"],
  ["MacAddress", "MAC address"],
  ["ComputerName", "Computer name"],
  ["Phase", "Phase"],
]);

const operatorLabels: Record<ConditionOperator, string> = {
  Equals: "is",
  NotEquals: "is not",
  StartsWith: "starts with",
  Contains: "contains",
};

export function describeCondition(condition: StepCondition): string {
  const variable = variableLabels.get(condition.variable) ?? condition.variable;

  return `${variable} ${operatorLabels[condition.operator]} "${condition.value}"`;
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

// The engine skips a step only when one of its conditions does not hold, and the agent does not say which, so
// the page shows each condition next to what the machine reports.
export function skipReason(
  step: DeploymentStepView,
  planned: SequenceStep | undefined,
  machine: MachineSummary | null,
): string {
  if (step.error !== null) {
    return step.error;
  }

  if (planned === undefined || planned.conditions.length === 0) {
    return "Skipped.";
  }

  const conditions = planned.conditions.map((condition) => {
    const value = machine === null ? null : reported(condition.variable, machine, step.phase);

    return value === null
      ? describeCondition(condition)
      : `${describeCondition(condition)}, and the machine reports "${value}"`;
  });

  return `Skipped, because not every condition held: ${conditions.join("; ")}.`;
}

export function plannedSteps(definition: SequenceDefinition | null): Map<string, SequenceStep> {
  return new Map((definition?.steps ?? []).map((step) => [step.id, step]));
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

function assignment(run: DeploymentSummary): string {
  const by = run.requestedBy ?? "an operator";

  switch (run.source) {
    case "Web":
      return `${by} assigned ${run.title} on the web`;
    case "Rule":
      return `${by} approved the machine on the web to run ${run.title}, which a rule chose`;
    case "Console":
      return `${by} signed in at the machine and chose ${run.title} there`;
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
  const entries: TimelineEntry[] = [];

  if (machine !== null && Date.parse(machine.firstSeenUtc) <= Date.parse(run.createdUtc)) {
    entries.push({
      key: "registered",
      utc: machine.firstSeenUtc,
      text:
        machine.firstSeenAddress === null
          ? "The machine registered for the first time"
          : `The machine registered for the first time, from ${machine.firstSeenAddress}`,
    });
  }

  entries.push({ key: "assigned", utc: run.createdUtc, text: assignment(run) });

  if (run.startedUtc !== null) {
    entries.push({ key: "started", utc: run.startedUtc, text: "The agent started the run" });
  }

  steps.forEach((step, position) => {
    if (step.finishedUtc === null || step.state !== "Done") {
      return;
    }

    // Skipped steps never start, so the machine was back when the next started step did.
    const next = steps.slice(position + 1).find((later) => later.startedUtc !== null) ?? null;
    const back = next?.startedUtc ?? null;
    const handsOver = step.phase === "WindowsPE" && steps[position + 1]?.phase === "Windows";
    const restarts = step.kind === "reboot" || planned.get(step.stepId)?.rebootAfter === true;

    if (handsOver) {
      entries.push({
        key: `handover-${step.stepId}`,
        utc: step.finishedUtc,
        text:
          back === null || next?.phase !== "Windows"
            ? "The hand-over to Windows began"
            : `Handed over to Windows; after Windows setup the agent continued there ${gap(step.finishedUtc, back)} later`,
      });
    } else if (restarts) {
      entries.push({
        key: `restart-${step.stepId}`,
        utc: step.finishedUtc,
        text:
          back === null
            ? `Restarted after step ${String(step.index + 1)}, ${step.name}`
            : `Restarted after step ${String(step.index + 1)}, ${step.name}; back after ${gap(step.finishedUtc, back)}`,
      });
    }
  });

  if (run.finishedUtc !== null) {
    entries.push({
      key: "finished",
      utc: run.finishedUtc,
      text:
        run.state === "Done"
          ? "The run is done"
          : run.state === "Cancelled"
            ? "The run was cancelled"
            : `The run failed${run.error === null ? "" : `: ${run.error}`}`,
    });
  }

  return entries.sort((a, b) => Date.parse(a.utc) - Date.parse(b.utc));
}
