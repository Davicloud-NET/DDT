// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { screen, waitFor, within } from "@testing-library/react";
import { expect } from "vitest";

import type { MachineSummary } from "@/machines/machines";
import type { MachineSequenceResolution } from "@/rules/rules";
import { chooseMenuItem, press } from "@/test/aria";
import {
  deploymentOptions,
  machineSummary,
  operator,
  sequenceResolution,
  sequenceSummary,
  sequenceView,
} from "@/test/builders";
import { renderPage, type RenderedPage } from "@/test/renderPage";
import { rowOf } from "@/test/rowOf";
import type { Routes } from "@/test/server";

export const now = new Date("2026-09-16T10:10:00Z");

export function secondsBefore(seconds: number): string {
  return new Date(now.getTime() - seconds * 1000).toISOString();
}

export const installWindowsId = "0193a4b2-0000-7000-8000-0000000000e1";
export const installLinuxId = "0193a4b2-0000-7000-8000-0000000000e2";
export const labPcsId = "0193a4b2-0000-7000-8000-0000000000e3";

export const installWindows = sequenceSummary({ id: installWindowsId });
export const labPcs = sequenceSummary({ id: labPcsId, name: "Lab PCs" });
export const linux = sequenceSummary({
  id: installLinuxId,
  name: "Install Linux",
  stepCount: 2,
  rawImageName: "noble",
  rawImageBootCapability: "NotSigned",
});

// A machine that registered and waits, as every machine starts.
export function waiting(overrides: Partial<MachineSummary> = {}): MachineSummary {
  return machineSummary({
    state: "Pending",
    everApproved: false,
    firstSeenUtc: "2026-09-16T10:00:00Z",
    lastSeenUtc: "2026-09-16T10:00:00Z",
    ...overrides,
  });
}

export function modelRule(
  overrides: Partial<MachineSequenceResolution> = {},
): MachineSequenceResolution {
  return sequenceResolution({
    source: "ModelRule",
    sequenceId: installWindowsId,
    sequenceName: "Install Windows",
    ruleId: "0193a4b2-0000-7000-8000-0000000000f1",
    explanation:
      "The rule for model Virtual Machine chooses Install Windows. A rule only chooses: the machine still needs an approval on the web, or someone who signs in at it, where the sequence is offered.",
    ...overrides,
  });
}

export const label = "Virtual Machine (00:15:5D:01:02:03)";

export function open(
  machines: MachineSummary[],
  routes: Routes = {},
  options: { user?: typeof operator; live?: boolean; narrow?: boolean; path?: string } = {},
): Promise<RenderedPage> {
  return renderPage({
    path: options.path ?? "/machines",
    user: options.user ?? operator,
    routes: { "GET /api/machines": { body: machines }, ...routes },
    ...(options.live === undefined ? {} : { live: options.live }),
    ...(options.narrow === undefined ? {} : { narrow: options.narrow }),
  });
}

// The row of the machine with this name; the name is the link to its page.
export function row(name: string): HTMLElement {
  return rowOf(screen.getByRole("link", { name }), "tr, li");
}

export function moreFor(name: string): HTMLElement {
  return screen.getByRole("button", { name: `More for ${name}` });
}

// The answers the assign dialog reads, plus what the test adds. It reads a chosen sequence's inputs from its document,
// and these ask nothing.
export function assigning(extra: Routes = {}): Routes {
  return {
    "GET /api/sequences": { body: [installWindows] },
    "GET /api/deployments/options": { body: deploymentOptions({ serverUtc: now.toISOString() }) },
    ...Object.fromEntries(
      [installWindows, labPcs, linux].map((sequence) => [
        `GET /api/sequences/${sequence.id}`,
        { body: sequenceView(sequence, []) },
      ]),
    ),
    ...extra,
  };
}

// Opens the assign dialog of the one machine on the page, from its key or, for a waiting machine, its menu.
export async function openAssign(viaMenu = false): Promise<HTMLElement> {
  await screen.findByRole("link", { name: "Virtual Machine" });

  if (viaMenu) {
    await chooseMenuItem(moreFor(label), "Assign a sequence");
  } else {
    press(within(row("Virtual Machine")).getByRole("button", { name: "Assign" }));
  }

  return screen.findByRole("dialog", { name: `Assign a task sequence to ${label}` });
}

export function assignKey(dialog: HTMLElement): HTMLElement {
  return within(dialog).getByRole("button", { name: "Assign sequence" });
}

export async function assignable(dialog: HTMLElement): Promise<void> {
  await waitFor(() => {
    expect(assignKey(dialog)).toBeEnabled();
  });
}

export function fact(scope: HTMLElement, name: string): string | null {
  return within(scope).getByText(name, { selector: "dt" }).nextElementSibling?.textContent ?? null;
}
