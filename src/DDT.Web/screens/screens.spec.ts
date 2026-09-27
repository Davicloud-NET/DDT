// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { expect, test, type Page } from "@playwright/test";

import type { SequenceDefinition } from "@/sequences/sequences";
import {
  currentUser,
  deploymentSummary,
  deploymentView,
  imageSummary,
  logLine,
  machineSummary,
  sequenceResolution,
  sequenceSummary,
  sequenceView,
  stepView,
} from "@/test/builders";

import installWindows from "../src/test/fixtures/install-windows.sequence.json" with { type: "json" };
import { sampleLogo } from "./sampleLogo";
import { serve } from "./server";

// A few pages as a browser draws them, in both themes: the design system's colours, type and spacing, which the page
// tests in jsdom cannot see. The clock stands still at 10:06 on the day of the fixtures, so relative times read the
// same every time.
const now = new Date("2026-09-16T10:06:00Z");
const administrator = currentUser("Administrator");
const machineId = "0193a4b2-0000-7000-8000-000000000001";
const runId = "0193a4b2-0000-7000-8000-0000000000d2";

const running = deploymentSummary({
  id: runId,
  state: "Running",
  stepCount: 5,
  stepIndex: 2,
  stepName: "Add the drivers for the model",
  percent: 45,
  phase: "WindowsPE",
  activity: "Step",
  createdUtc: "2026-09-16T10:00:00Z",
  startedUtc: "2026-09-16T10:01:00Z",
  updatedUtc: "2026-09-16T10:05:30Z",
});

const machines = [
  machineSummary({
    id: machineId,
    state: "Deploying",
    assignedName: "PC-042",
    manufacturer: "LENOVO",
    model: "ThinkPad T14 Gen 4",
    deviceKind: "Laptop",
    primaryMac: "8C1645A0B2C4",
    lastSeenAddress: "10.0.4.51",
    firstSeenUtc: "2026-09-15T09:00:00Z",
    lastSeenUtc: "2026-09-16T10:05:30Z",
    deployment: running,
  }),
  machineSummary({
    id: "0193a4b2-0000-7000-8000-000000000002",
    state: "Pending",
    everApproved: false,
    primaryMac: "00155D010207",
    lastSeenAddress: "172.25.132.98",
    deviceKind: "Virtual",
    firstSeenUtc: "2026-09-16T10:04:00Z",
    lastSeenUtc: "2026-09-16T10:05:50Z",
  }),
  machineSummary({
    id: "0193a4b2-0000-7000-8000-000000000003",
    state: "Approved",
    assignedName: "PC-017",
    manufacturer: "Dell Inc.",
    model: "OptiPlex 7010",
    deviceKind: "Desktop",
    primaryMac: "B04F13C2D3E4",
    lastSeenAddress: "10.0.4.63",
    lastSeenUtc: "2026-09-16T09:12:00Z",
    deployment: deploymentSummary({
      id: "0193a4b2-0000-7000-8000-0000000000d3",
      state: "Done",
      stepCount: 5,
      percent: 100,
      startedUtc: "2026-09-16T08:40:00Z",
      finishedUtc: "2026-09-16T09:12:00Z",
      updatedUtc: "2026-09-16T09:12:00Z",
    }),
  }),
  machineSummary({
    id: "0193a4b2-0000-7000-8000-000000000004",
    state: "Approved",
    assignedName: "PC-018",
    manufacturer: "Dell Inc.",
    model: "OptiPlex 7010",
    deviceKind: "Desktop",
    primaryMac: "B04F13C2D3F0",
    lastSeenAddress: "10.0.4.64",
    lastSeenUtc: "2026-09-16T09:30:00Z",
    deployment: deploymentSummary({
      id: "0193a4b2-0000-7000-8000-0000000000d4",
      state: "Failed",
      stepCount: 5,
      stepIndex: 3,
      stepName: "Add the drivers for the model",
      percent: 60,
      startedUtc: "2026-09-16T09:00:00Z",
      finishedUtc: "2026-09-16T09:30:00Z",
      updatedUtc: "2026-09-16T09:30:00Z",
      error: "No driver package targets Dell Inc. OptiPlex 7010.",
    }),
  }),
];

const definition = installWindows.definition as SequenceDefinition;
const phases = definition.steps.map((step) =>
  step.kind === "joinDomain" ? "Windows" : "WindowsPE",
);

const run = deploymentView({
  summary: running,
  machineId,
  definition,
  steps: definition.steps.map((step, index) =>
    stepView({
      stepId: step.id,
      index,
      name: step.name,
      kind: step.kind,
      phase: phases[index],
      ...(index < 2
        ? {
            state: "Done",
            percent: 100,
            startedUtc: `2026-09-16T10:0${String(index + 1)}:00Z`,
            finishedUtc: `2026-09-16T10:0${String(index + 2)}:00Z`,
          }
        : index === 2
          ? { state: "Running", percent: 45, startedUtc: "2026-09-16T10:03:00Z" }
          : {}),
    }),
  ),
});

const lines = [
  "Partitioned disk 0 as GPT: EFI 300 MB, MSR 16 MB, Windows, recovery 1024 MB.",
  "Downloading Windows 11 Pro, 4.6 GB.",
  "Applied Windows 11 Pro to W:\\ in 2 min 41 s.",
  "Model LENOVO ThinkPad T14 Gen 4: 3 driver packages match.",
  "Adding drivers from Lenovo T14 Gen 4 (1 of 3).",
].map((message, index) => logLine(index + 1, { message, deploymentId: runId }));

async function show(
  page: Page,
  path: string,
  answers: Record<string, unknown>,
  signedIn = true,
): Promise<void> {
  await page.clock.setFixedTime(now);
  const unanswered = await serve(page, signedIn ? administrator : null, answers);
  await page.goto(path);
  await page.evaluate(() => document.fonts.ready);
  expect(unanswered).toEqual([]);
}

for (const scheme of ["light", "dark"] as const) {
  test.describe(scheme, () => {
    test.use({ colorScheme: scheme });

    test("sign-in", async ({ page }) => {
      await show(page, "/sign-in", { "GET /api/auth/external/providers": [] }, false);
      await expect(page.getByRole("button", { name: "Sign in" })).toBeVisible();

      await expect(page).toHaveScreenshot(`sign-in-${scheme}.png`);
    });

    test("machines", async ({ page }) => {
      await show(page, "/machines", { "GET /api/machines": machines });
      await expect(page.getByText("PC-042")).toBeVisible();

      await expect(page).toHaveScreenshot(`machines-${scheme}.png`);
    });

    test("a machine's run", async ({ page }) => {
      await show(page, `/machines/${machineId}`, {
        "GET /api/machines": machines,
        [`GET /api/machines/${machineId}/deployments`]: [running],
        [`GET /api/machines/${machineId}/sequence`]: sequenceResolution({
          source: "Assigned",
          sequenceId: installWindows.key,
          sequenceName: "Install Windows",
          explanation: "admin assigned Install Windows on the web, which comes before every rule.",
        }),
        [`GET /api/deployments/${runId}`]: run,
        [`GET /api/machines/${machineId}/log`]: { lines, hasOlder: false },
      });
      await expect(page.getByText("Adding drivers from Lenovo T14 Gen 4 (1 of 3).")).toBeVisible();

      await expect(page).toHaveScreenshot(`machine-run-${scheme}.png`);
    });
  });
}

test("a task sequence", async ({ page }) => {
  const summary = sequenceSummary({ stepCount: definition.steps.length, continuesInWindows: true });

  await show(page, `/deployment/sequences/${summary.id}`, {
    [`GET /api/sequences/${summary.id}`]: {
      ...sequenceView(summary, definition.steps),
      stepPhases: phases,
    },
    "GET /api/sequences": [summary],
    "GET /api/images": [imageSummary({ name: "Windows 11 Pro 25H2" })],
    "GET /api/packages": [],
    "GET /api/deployments/options": {
      domainConfigured: true,
      requireWebApproval: false,
      zeroTouchEnabled: false,
      serverUtc: now.toISOString(),
    },
  });
  await expect(page.getByRole("heading", { name: "Install Windows" })).toBeVisible();

  await expect(page).toHaveScreenshot("sequence-light.png");
});

// The deployment defaults, and the console's logo on their page.
const deploymentDefaults = {
  "GET /api/settings/deployment": {
    section: "deployment",
    version: 4,
    updatedUtc: "2026-09-15T16:20:00Z",
    updatedBy: "admin",
    values: {
      timeZone: "W. Europe Standard Time",
      locale: "de-DE",
      keyboard: "0407:00000407",
      consoleLanguage: "de",
      localAdministrator: { name: "Admin" },
      domain: {
        name: "corp.example",
        organizationalUnit: "OU=Workstations,DC=corp,DC=example",
        userName: "CORP\\ddt-join",
        controller: null,
      },
    },
    secrets: {
      "localAdministrator.password": {
        isSet: true,
        unreadable: false,
        updatedUtc: "2026-09-15T16:20:00Z",
      },
      "domain.password": { isSet: true, unreadable: false, updatedUtc: "2026-09-15T16:20:00Z" },
    },
    locked: [],
    problems: [],
    warnings: [],
    apply: null,
    reauthenticate: [],
  },
  "GET /api/settings/console-logo": {
    sha256: "5d41402abc4b2a76b9719d911017c5925d41402abc4b2a76b9719d911017c592",
    size: 1_804,
    width: 240,
    height: 64,
    uploadedUtc: "2026-09-15T16:24:00Z",
    uploadedBy: "admin",
  },
  "GET /api/settings/console-logo/image": sampleLogo(),
};

test.describe("dark", () => {
  test.use({ colorScheme: "dark" });

  test("deployment defaults", async ({ page }) => {
    await show(page, "/deployment/defaults", deploymentDefaults);
    await expect(page.getByRole("heading", { name: "The console at the machine" })).toBeVisible();

    await expect(page).toHaveScreenshot("deployment-defaults-dark.png");
  });
});

test("the console's logo", async ({ page }) => {
  await show(page, "/deployment/defaults", deploymentDefaults);
  const panel = page
    .getByRole("heading", { name: "Logo on the console" })
    .locator("xpath=ancestor::section[1]");
  await panel.scrollIntoViewIfNeeded();
  await expect(
    panel.getByRole("img", { name: "The console's header with the logo" }).locator("img"),
  ).toHaveJSProperty("complete", true);

  await expect(panel).toHaveScreenshot("console-logo-light.png");
});

test.describe("phone", () => {
  test.use({ viewport: { width: 390, height: 844 }, hasTouch: true, isMobile: true });

  test("machines", async ({ page }) => {
    await show(page, "/machines", { "GET /api/machines": machines });
    await expect(page.getByText("PC-042")).toBeVisible();

    await expect(page).toHaveScreenshot("machines-phone.png");
  });
});
