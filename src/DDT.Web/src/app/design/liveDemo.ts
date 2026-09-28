// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { queryOptions } from "@tanstack/react-query";

import type { RailStep } from "@/ui/SequenceRail";
import type { StateTone } from "@/ui/StateTag";

// A list patched as the hub patches one, to see the flash of a changed row, a new row entering and the rail filling.
export interface DemoMachine {
  id: string;
  name: string;
  state: "Waiting" | "Deploying" | "Done" | "Failed";
  step: number;
  percent: number;
}

export const demoTones: Record<DemoMachine["state"], StateTone> = {
  Waiting: "attention",
  Deploying: "run",
  Done: "ok",
  Failed: "fail",
};

const demoSteps = 4;

export const demoQuery = queryOptions({
  queryKey: ["design-live-demo"],
  queryFn: () =>
    Promise.resolve<DemoMachine[]>([
      { id: "a", name: "LAB-PC-014", state: "Deploying", step: 1, percent: 30 },
      { id: "b", name: "BUILD-VM-02", state: "Deploying", step: 2, percent: 70 },
    ]),
  staleTime: Infinity,
});

export function demoRail(machine: DemoMachine): RailStep[] {
  return Array.from({ length: demoSteps }, (_, index): RailStep => {
    if (machine.state === "Done" || index < machine.step) {
      return { state: "done" };
    }

    if (index > machine.step || machine.state === "Waiting") {
      return { state: "waiting" };
    }

    return machine.state === "Failed"
      ? { state: "failed" }
      : { state: "running", percent: machine.percent };
  });
}

export function advance(machine: DemoMachine): DemoMachine {
  if (machine.state !== "Deploying") {
    return machine;
  }

  if (machine.percent < 100) {
    return { ...machine, percent: Math.min(100, machine.percent + 35) };
  }

  return machine.step + 1 < demoSteps
    ? { ...machine, step: machine.step + 1, percent: 0 }
    : { ...machine, state: "Done" };
}
