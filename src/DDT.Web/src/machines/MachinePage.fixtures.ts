// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { screen, within } from "@testing-library/react";

import type {
  DeploymentStepView,
  DeploymentSummary,
  DeploymentView,
} from "@/deployments/deployments";
import type { MachineLogEntry } from "@/log/log";
import type { MachineSummary } from "@/machines/machines";
import type { SequenceStep, StepKind } from "@/sequences/sequences";
import { newStep } from "@/sequences/steps";
import {
  deploymentSummary,
  deploymentView,
  machineSummary,
  operator,
  sequenceResolution,
  stepView,
} from "@/test/builders";
import { renderPage, type RenderedPage } from "@/test/renderPage";
import type { Answer, Routes } from "@/test/server";

export const now = new Date("2026-09-16T10:06:00Z");
export const machineId = "0193a4b2-0000-7000-8000-000000000001";
export const runId = "0193a4b2-0000-7000-8000-0000000000d2";
export const olderRunId = "0193a4b2-0000-7000-8000-0000000000d1";
export const newerRunId = "0193a4b2-0000-7000-8000-0000000000d3";

export function run(overrides: Partial<DeploymentSummary> = {}): DeploymentSummary {
  return deploymentSummary({
    id: runId,
    state: "Running",
    stepCount: 6,
    stepIndex: 4,
    stepName: "Apply Windows 11",
    percent: 45,
    phase: "WindowsPE",
    activity: "Step",
    createdUtc: "2026-09-16T10:00:00Z",
    startedUtc: "2026-09-16T10:01:00Z",
    updatedUtc: "2026-09-16T10:05:30Z",
    ...overrides,
  });
}

export function machine(overrides: Partial<MachineSummary> = {}): MachineSummary {
  return machineSummary({
    id: machineId,
    state: "Deploying",
    assignedName: "PC-042",
    firstSeenUtc: "2026-09-15T09:00:00Z",
    lastSeenUtc: "2026-09-16T10:05:30Z",
    deployment: run(),
    ...overrides,
  });
}

export function planned(
  id: string,
  name: string,
  kind: StepKind,
  more: Partial<Pick<SequenceStep, "conditions" | "continueOnError" | "rebootAfter">> = {},
): SequenceStep {
  return { ...newStep(kind, id), name, ...more };
}

export function step(
  index: number,
  id: string,
  name: string,
  kind: string,
  more: Partial<DeploymentStepView> = {},
): DeploymentStepView {
  return stepView({ stepId: id, index, name, kind, ...more });
}

export const applyImage = step(4, "s5", "Apply Windows 11", "applyImage", {
  state: "Running",
  percent: 45,
  startedUtc: "2026-09-16T10:05:00Z",
});

export function view(overrides: Partial<DeploymentView> = {}): DeploymentView {
  return deploymentView({
    summary: run(),
    machineId,
    definition: {
      version: 1,
      steps: [
        planned("s1", "Partition", "partition"),
        planned("s2", "Check model", "runScript", {
          conditions: [{ variable: "Model", operator: "Equals", value: "Latitude 7440" }],
        }),
        planned("s3", "Optional tool", "runScript", { continueOnError: true }),
        planned("s4", "Restart once", "reboot"),
        planned("s5", "Apply Windows 11", "applyImage"),
        planned("s6", "Set wallpaper", "runScript"),
      ],
    },
    steps: [
      step(0, "s1", "Partition", "partition", {
        state: "Done",
        percent: 100,
        startedUtc: "2026-09-16T10:01:00Z",
        finishedUtc: "2026-09-16T10:01:30Z",
      }),
      step(1, "s2", "Check model", "runScript", {
        state: "Skipped",
        finishedUtc: "2026-09-16T10:01:31Z",
      }),
      step(2, "s3", "Optional tool", "runScript", {
        state: "Failed",
        startedUtc: "2026-09-16T10:01:32Z",
        finishedUtc: "2026-09-16T10:02:00Z",
        error: "The script ended with exit code 3.",
      }),
      step(3, "s4", "Restart once", "reboot", {
        state: "Done",
        percent: 100,
        startedUtc: "2026-09-16T10:02:30Z",
        finishedUtc: "2026-09-16T10:03:00Z",
      }),
      applyImage,
      step(5, "s6", "Set wallpaper", "runScript", { phase: "Windows" }),
    ],
    ...overrides,
  });
}

export function logOf(deploymentId: string | null): string {
  return `GET /api/machines/${machineId}/log?limit=500${deploymentId === null ? "" : `&deploymentId=${deploymentId}`}`;
}

// What the page reads: the machine list, the machine's runs, what chooses its sequence, each run and its log.
export function answers(
  machines: MachineSummary[],
  history: DeploymentSummary[],
  runs: DeploymentView[],
  lines: MachineLogEntry[] = [],
): Routes {
  return {
    "GET /api/machines": { body: machines },
    [`GET /api/machines/${machineId}/deployments`]: { body: history },
    [`GET /api/machines/${machineId}/sequence`]: {
      body: sequenceResolution({
        source: "Assigned",
        explanation: "operator assigned Install Windows on the web, which comes before every rule.",
      }),
    },
    ...Object.fromEntries(
      runs.flatMap((detail): [string, Answer][] => [
        [`GET /api/deployments/${detail.summary.id}`, { body: detail }],
        [
          logOf(detail.summary.id),
          {
            body: {
              lines: lines.filter((line) => line.deploymentId === detail.summary.id),
              hasOlder: false,
            },
          },
        ],
      ]),
    ),
  };
}

export function open(
  routes: Routes,
  options: { path?: string; live?: boolean; user?: typeof operator } = {},
): Promise<RenderedPage> {
  return renderPage({
    path: options.path ?? `/machines/${machineId}`,
    user: options.user ?? operator,
    routes,
    ...(options.live === undefined ? {} : { live: options.live }),
  });
}

export function stepsList(): HTMLElement {
  return screen.getByRole("list", { name: "Steps" });
}

// The step with this name in the list of the run's steps.
export function stepRow(name: string): HTMLElement {
  const item = within(stepsList()).getByText(name, { selector: "span" }).closest("li");

  if (item === null) {
    throw new Error(`${name} is not a step.`);
  }

  return item;
}

export function history(): HTMLElement {
  return screen.getByRole("list", { name: "Runs of this machine" });
}

export function panelOf(heading: string): HTMLElement {
  const panel = screen.getByRole("heading", { name: heading }).closest("section");

  if (panel === null) {
    throw new Error(`${heading} is not a panel.`);
  }

  return panel;
}
