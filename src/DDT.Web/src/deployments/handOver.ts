// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";

import type { MachineSummary } from "@/machines/machines";

import { activityLabel } from "./deployments";

// How long a first start of the installed Linux may take before a page lists what could keep DDT's agent from calling.
// On virtual machines it called 10 to 17 s after the hand-over. A firmware's start and the packages cloud-init
// installs take minutes on real ones.
export const FIRST_CALL_EXPECTED_MS = 10 * 60_000;

// Since when the machine's Linux run waits for the first call from the installed Linux, or null when it does not.
// The server says it from the boot environment's last report, until the agent registers from the installed Linux.
export function linuxHandOver(
  machine: Pick<MachineSummary, "handedOverUtc" | "deployment">,
): string | null {
  const run = machine.deployment;

  return run?.state === "Running" && run.phase === "Linux" ? (machine.handedOverUtc ?? null) : null;
}

// How often the machine has started from the network again since its Linux run was handed over, while the installed
// Linux has not called. The boot environment sent it back to its disk each time, at the time linuxHandOver names.
export function linuxReturns(
  machine: Pick<MachineSummary, "handedOverUtc" | "bootReturns" | "deployment">,
): number {
  return linuxHandOver(machine) === null ? 0 : (machine.bootReturns ?? 0);
}

// At how many starts from the network in a row the server ends a run that goes on in the installed Linux. It is the
// server's NetworkDetours.Max.
export const MAX_NETWORK_DETOURS = 5;

// How often in a row the machine started from the network while its run goes on in the installed Linux, with no call
// from there in between. Each time it was sent back to its disk. The installed Linux's next call sets it back to 0.
export function linuxDetours(machine: Pick<MachineSummary, "detours" | "deployment">): number {
  const run = machine.deployment;

  return run?.state === "Running" && run.phase === "Linux" ? (machine.detours ?? 0) : 0;
}

// What the machine's run does between its steps, as a heading. After a hand-over to Linux the machine is awaited,
// not known to restart. Null while a step runs, and without a run.
export function activityOf(
  machine: Pick<MachineSummary, "handedOverUtc" | "deployment">,
): string | null {
  const run = machine.deployment;

  if (run === null) {
    return null;
  }

  return linuxHandOver(machine) === null
    ? activityLabel(run.activity, run.phase)
    : t`Waiting for the installed Linux`;
}

// Whether the installed Linux has been silent for longer than a first start usually takes.
export function isOverdue(sinceUtc: string, now: number): boolean {
  return now - Date.parse(sinceUtc) >= FIRST_CALL_EXPECTED_MS;
}
