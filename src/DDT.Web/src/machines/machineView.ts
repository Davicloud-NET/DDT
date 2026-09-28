// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { MessageDescriptor } from "@lingui/core";
import { msg, t } from "@lingui/core/macro";

import { isWaiting, type DeploymentSummary, type DeploymentView } from "@/deployments/deployments";
import { runPath, type PathState, type RunPath } from "@/runs/runPath";
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

// Pending and Approved are the server's words. On the page, a pending machine is waiting for someone, and an
// approved one without a run is ready for one.
export const stateLabel: Record<MachineState, MessageDescriptor> = {
  Pending: msg`Waiting`,
  Approved: msg`Ready`,
  Deploying: msg`Deploying`,
  Done: msg`Done`,
  Failed: msg`Failed`,
  Rejected: msg`Rejected`,
  Retired: msg`Retired`,
};

// The state a machine shows. It's the machine's own state, unless its run waits for someone, for answers or at a
// pause. That asks for attention just like a waiting machine.
export function machineTag(machine: MachineSummary): {
  tone: StateTone;
  label: MessageDescriptor;
} {
  const run = machine.deployment;

  if (isWaiting(run)) {
    return {
      tone: "attention",
      label:
        run?.activity === "WaitingForInput"
          ? msg`Needs answers`
          : run?.activity === "Paused"
            ? msg`Paused`
            : msg`Needs someone`,
    };
  }

  return { tone: stateTone[machine.state], label: stateLabel[machine.state] };
}

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

// A machine whose run waits for someone is among the waiting ones, not the deploying ones.
export function inFilter(machine: MachineSummary, filter: MachineFilter): boolean {
  return (
    filter === "all" ||
    (isWaiting(machine.deployment) ? "waiting" : filterOfState[machine.state]) === filter
  );
}

// Machines that need someone come first, then running ones, then idle ones. Within a state, the newest machine
// comes first. Last seen isn't used, because it changes on every contact and would move rows under the pointer.
const rank: Record<MachineState, number> = {
  Pending: 0,
  Failed: 1,
  Deploying: 2,
  Approved: 3,
  Done: 4,
  Rejected: 5,
  Retired: 6,
};

function rankOf(machine: MachineSummary): number {
  return isWaiting(machine.deployment) ? rank.Pending : rank[machine.state];
}

export function byAttention(a: MachineSummary, b: MachineSummary): number {
  return (
    rankOf(a) - rankOf(b) ||
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

// The line under the name. It shows whichever of maker, model and serial number the name leaves out.
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

// Builds the rail from the list's summary of a run. The summary only knows the current step, so earlier steps
// count as done and later ones as waiting. A run's own page draws the rail from its steps instead, skipped ones
// included.
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
        return run.activity === "Paused"
          ? { state: "paused" }
          : { state: "running", percent: run.percent };
      }
    }

    return { state: "waiting" };
  });
}

const railStates: Record<Exclude<PathState, "notTaken">, RailStepState> = {
  waiting: "waiting",
  running: "running",
  paused: "paused",
  done: "done",
  failed: "failed",
  skipped: "skipped",
};

// The rail of a run's path: the leaf steps it went through, the one it's at, and those still ahead. Each has its
// number in the sequence. Steps in branches the run didn't take are left out.
export function railFromPath(path: RunPath): RailStep[] {
  return path.leaves.flatMap((leaf): RailStep[] =>
    leaf.state === "notTaken"
      ? []
      : [
          {
            state: railStates[leaf.state],
            ...(leaf.state === "running" ? { percent: leaf.step?.percent ?? 0 } : {}),
            name: leaf.step?.name ?? leaf.node.name,
            ...(leaf.entry.number === null ? {} : { number: leaf.entry.number }),
          },
        ],
  );
}

export function railFromView(view: DeploymentView): RailStep[] {
  return railFromPath(
    runPath(view.definition, view.steps, {
      activity: view.summary.activity,
      pause: view.pause ?? null,
    }),
  );
}

// The rail in words, for screen readers and as its tooltip. A run can end before its first step, for example when
// the check before it failed or the assignment was cancelled. Then no step is done, so the text names none.
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

// One step of the rail in words, for screen readers: its number, its name if it has one, and its state.
export function railStepText(step: RailStep, index: number): string {
  const number = step.number ?? index + 1;
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
