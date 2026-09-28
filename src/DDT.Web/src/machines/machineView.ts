// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { MessageDescriptor } from "@lingui/core";
import { msg, t } from "@lingui/core/macro";

import type { DeploymentStepView, DeploymentSummary } from "@/deployments/deployments";
import type { StepState } from "@/sequences/sequences";
import type { DeviceKind } from "@/ui/DeviceGlyph";
import type { RailStep, RailStepState } from "@/ui/SequenceRail";
import type { StateTone } from "@/ui/StateTag";

import { formatMac, type MachineState, type MachineSummary } from "./machines";

// How the machine pages show a machine: the words and tones of its states, the filters, the order, the search and
// the rail of its run.

export const stateTone: Record<MachineState, StateTone> = {
  Pending: "attention",
  Approved: "idle",
  Deploying: "run",
  Done: "ok",
  Failed: "fail",
  Rejected: "retired",
  Retired: "retired",
};

// Pending and Approved are the server's words; to the person at the page, a pending machine waits for someone,
// and an approved one without a run is ready for one.
export const stateLabel: Record<MachineState, MessageDescriptor> = {
  Pending: msg`Waiting`,
  Approved: msg`Ready`,
  Deploying: msg`Deploying`,
  Done: msg`Done`,
  Failed: msg`Failed`,
  Rejected: msg`Rejected`,
  Retired: msg`Retired`,
};

export type MachineFilter =
  "all" | "deploying" | "waiting" | "failed" | "done" | "ready" | "retired";

export const machineFilters: {
  id: MachineFilter;
  label: MessageDescriptor;
  tone?: "attention" | "fail";
}[] = [
  { id: "all", label: msg`All` },
  { id: "deploying", label: msg`Deploying` },
  { id: "waiting", label: msg`Waiting`, tone: "attention" },
  { id: "failed", label: msg`Failed`, tone: "fail" },
  { id: "done", label: msg`Done` },
  { id: "ready", label: msg`Ready` },
  { id: "retired", label: msg`Retired` },
];

const filterOfState: Record<MachineState, Exclude<MachineFilter, "all">> = {
  Pending: "waiting",
  Approved: "ready",
  Deploying: "deploying",
  Done: "done",
  Failed: "failed",
  Rejected: "retired",
  Retired: "retired",
};

export function isMachineFilter(value: unknown): value is MachineFilter {
  return typeof value === "string" && machineFilters.some((filter) => filter.id === value);
}

export function inFilter(machine: MachineSummary, filter: MachineFilter): boolean {
  return filter === "all" || filterOfState[machine.state] === filter;
}

// What needs someone comes first, then what runs, then what rests. Within a state the newest machine comes first;
// last seen is not used, because it changes on every contact and would move rows under the pointer.
const rank: Record<MachineState, number> = {
  Pending: 0,
  Failed: 1,
  Deploying: 2,
  Approved: 3,
  Done: 4,
  Rejected: 5,
  Retired: 6,
};

export function byAttention(a: MachineSummary, b: MachineSummary): number {
  return (
    rank[a.state] - rank[b.state] ||
    Date.parse(b.firstSeenUtc) - Date.parse(a.firstSeenUtc) ||
    a.id.localeCompare(b.id)
  );
}

// The words a person may type to find a machine: its name, model, serial, addresses and identifiers. A MAC
// matches with or without separators.
export function matchesSearch(machine: MachineSummary, query: string): boolean {
  const words = query.toLowerCase().split(/\s+/).filter(Boolean);

  if (words.length === 0) {
    return true;
  }

  const haystack = [
    machine.assignedName,
    machine.manufacturer,
    machine.model,
    machine.serialNumber,
    machine.lastSeenAddress,
    machine.smbiosUuid,
    ...machine.macAddresses.flatMap((mac) => [mac, formatMac(mac)]),
    machine.deployment?.title,
  ]
    .filter((value): value is string => typeof value === "string")
    .join(" ")
    .toLowerCase();

  return words.every(
    (word) => haystack.includes(word.replace(/[-:]/g, "")) || haystack.includes(word),
  );
}

// The name a list shows: the computer's name once it has one, else its model.
export function displayName(machine: MachineSummary): string {
  return machine.assignedName ?? machine.model ?? t`Unknown model`;
}

// The line under the name: what the name leaves out, among maker, model and serial number.
export function hardwareLine(machine: MachineSummary): string {
  const maker =
    machine.assignedName === null ? machine.manufacturer : (machine.model ?? machine.manufacturer);
  const serial = machine.serialNumber;

  if (maker !== null && serial !== null) {
    return t`${maker}, serial ${serial}`;
  }

  return maker ?? (serial === null ? formatMac(machine.primaryMac) : t`Serial ${serial}`);
}

export function deviceKind(machine: MachineSummary): DeviceKind {
  switch (machine.deviceKind) {
    case "Laptop":
      return "laptop";
    case "Desktop":
      return "desktop";
    case "Tablet":
      return "tablet";
    case "Server":
      return "server";
    case "Virtual":
      return "virtual";
    default:
      return "unknown";
  }
}

// The rail from the list's summary of a run, which knows only the step it is on: the steps before it count as done
// and those after it as waiting. A run's own page draws the rail from its steps instead, skipped ones included.
export function railFromSummary(run: DeploymentSummary): RailStep[] {
  const count = Math.max(run.stepCount, 0);
  const at = run.stepIndex ?? -1;

  return Array.from({ length: count }, (_, index): RailStep => {
    if (run.state === "Done") {
      return { state: "done" };
    }

    if (index < at) {
      return { state: "done" };
    }

    if (index === at) {
      if (run.state === "Failed") {
        return { state: "failed" };
      }

      if (run.state === "Running") {
        return { state: "running", percent: run.percent };
      }
    }

    return { state: "waiting" };
  });
}

const railStates: Record<StepState, RailStepState> = {
  Pending: "waiting",
  Running: "running",
  Done: "done",
  Failed: "failed",
  Skipped: "skipped",
};

export function railFromSteps(steps: readonly DeploymentStepView[]): RailStep[] {
  return [...steps]
    .sort((a, b) => a.index - b.index)
    .map((step) => ({
      state: railStates[step.state],
      ...(step.state === "Running" ? { percent: step.percent } : {}),
      name: step.name,
    }));
}

// The rail in words, for screen readers and as its tooltip. A run that ended before its first step, as when the
// check before it failed or the assignment was cancelled, shows no step done, so it names none.
export function railLabel(run: DeploymentSummary): string {
  const count = run.stepCount;
  const number = (run.stepIndex ?? 0) + 1;
  const percent = run.percent;

  switch (run.state) {
    case "Done":
      return t`All ${count} steps done`;
    case "Failed":
      return run.stepIndex === null
        ? t`Not started, ${count} steps`
        : t`Failed at step ${number} of ${count}`;
    case "Running":
      return run.stepIndex === null
        ? t`Starting, ${count} steps`
        : t`Step ${number} of ${count} running, ${percent} percent`;
    case "Assigned":
      return t`Not started, ${count} steps`;
    case "Cancelled":
      return run.stepIndex === null
        ? t`Not started, ${count} steps`
        : t`Stopped at step ${number} of ${count}`;
  }
}

// One step of the rail in words, for screen readers: its number, its name when there is one, and how it stands.
export function railStepText(step: RailStep, index: number): string {
  const number = index + 1;
  const name = typeof step.name === "string" ? step.name : null;
  const percent = step.percent ?? 0;

  switch (step.state) {
    case "done":
      return name === null ? t`Step ${number}, done` : t`Step ${number}, ${name}, done`;
    case "running":
      return name === null
        ? t`Step ${number}, running, ${percent} percent`
        : t`Step ${number}, ${name}, running, ${percent} percent`;
    case "failed":
      return name === null ? t`Step ${number}, failed` : t`Step ${number}, ${name}, failed`;
    case "skipped":
      return name === null ? t`Step ${number}, skipped` : t`Step ${number}, ${name}, skipped`;
    case "waiting":
      return name === null
        ? t`Step ${number}, not started`
        : t`Step ${number}, ${name}, not started`;
    case "paused":
      return name === null ? t`Step ${number}, paused` : t`Step ${number}, ${name}, paused`;
  }
}
