// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { expect, test, type Page } from "@playwright/test";

import {
  currentUser,
  deploymentSummary,
  imageSummary,
  logLine,
  machineSummary,
  sequenceResolution,
  sequenceSummary,
  sequenceView,
} from "@/test/builders";

import { flowDefinition, flowPhases, flowProblems, windowsImageId } from "@/test/flowSequence";
import { treeMachine, treeMachineId, treeRunId, treeRunView } from "@/test/treeRun";
import { node } from "@/test/treeSequence";

import {
  accounts,
  roles,
  rules,
  sequences as ruleSequences,
  testedMachine,
  testedResolution,
} from "./rules";
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

// A run of a tree that waits at a pause, as the design canvas draws one: the IF took Then, the office's printer step
// was skipped on a laptop, and the share test went round twice.
const tree = treeRunView();
const treeLines = [
  ["Partitioned disk 0 as GPT: EFI 300 MB, MSR 16 MB, Windows, recovery 1024 MB.", node.partition],
  [
    "Is it a Latitude? Model contains Latitude holds for Latitude 7450, so Then runs.",
    node.latitude,
  ],
  ["Applied Windows 11 for Latitudes to W:\\ in 3 min 0 s.", node.applyLatitude],
  ["Joined corp.example as PC-G2341KXQ.", node.join],
  ["Test the share: exit code 1, going round again (2 of at most 5).", node.test],
  ["Paused: Stick the asset tag on the lid and note it in the inventory.", node.pause],
].map(([message, stepId], index) =>
  logLine(index + 1, { message, stepId, deploymentId: treeRunId }),
);

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

    // The design canvas's run artboard is 1440 by 1080 pixels.
    test("a machine's run", async ({ page }) => {
      await page.setViewportSize({ width: 1440, height: 1080 });
      await show(page, `/machines/${treeMachineId}`, {
        "GET /api/machines": [treeMachine(), ...machines],
        [`GET /api/machines/${treeMachineId}/deployments`]: [tree.summary],
        [`GET /api/machines/${treeMachineId}/sequence`]: sequenceResolution({
          source: "Assigned",
          sequenceId: tree.summary.sequenceId,
          sequenceName: tree.summary.title,
          explanation:
            "anna assigned Windows 11 office PCs on the web, which comes before every rule.",
        }),
        [`GET /api/deployments/${treeRunId}`]: tree,
        [`GET /api/machines/${treeMachineId}/log`]: { lines: treeLines, hasOlder: false },
      });
      await expect(page.getByRole("group", { name: "Flow of this run" })).toBeVisible();
      await expect(page.getByText("Joined corp.example as PC-G2341KXQ.")).toBeAttached();

      await expect(page).toHaveScreenshot(`machine-run-${scheme}.png`);
    });
  });
}

// The flow builder with a sequence of every shape: an IF, a group, a repeat, a template and a problem, the IF chosen.
const flowSummary = sequenceSummary({
  name: "Windows 11 office PCs",
  stepCount: 12,
  problemCount: flowProblems.length,
  continuesInWindows: true,
});
const facts = [
  ["Manufacturer", "Text"],
  ["Model", "Text"],
  ["FriendlyModel", "Text"],
  ["SerialNumber", "Text"],
  ["SmbiosUuid", "Text"],
  ["DeviceKind", "Text"],
  ["MacAddress", "Mac"],
  ["PrimaryMacAddress", "Mac"],
  ["ComputerName", "Text"],
  ["Phase", "Text"],
  ["MemoryMegabytes", "Number"],
  ["ProcessorName", "Text"],
  ["ProcessorCores", "Number"],
  ["LogicalProcessors", "Number"],
  ["TpmPresent", "YesNo"],
  ["TpmVersion", "Number"],
  ["SecureBootCapable", "YesNo"],
  ["SecureBootEnabled", "YesNo"],
  ["IPv4Address", "IPv4"],
  ["IPv4PrefixLength", "Number"],
  ["Subnet", "Text"],
  ["DefaultGateway", "IPv4"],
  ["DnsSuffix", "Text"],
  ["DhcpServer", "IPv4"],
  ["LastStepFailed", "YesNo"],
  ["LastExitCode", "Number"],
].map(([name, type]) => ({
  name,
  type,
  changesDuringRun: name === "Phase" || name === "LastStepFailed" || name === "LastExitCode",
}));
const builderAnswers = {
  [`GET /api/sequences/${flowSummary.id}`]: {
    ...sequenceView(flowSummary, flowDefinition.steps),
    definition: flowDefinition,
    nodePhases: flowPhases,
    problems: flowProblems,
  },
  "GET /api/sequences": [flowSummary],
  "GET /api/sequences/facts": facts,
  "GET /api/rules": [],
  "GET /api/machine-roles": [],
  "GET /api/accounts": [],
  "GET /api/images": [imageSummary({ id: windowsImageId, name: "Windows 11 Pro 25H2" })],
  "GET /api/packages": [],
  "GET /api/deployments/options": {
    domainConfigured: true,
    requireWebApproval: false,
    zeroTouchEnabled: false,
    serverUtc: now.toISOString(),
  },
};

for (const scheme of ["light", "dark"] as const) {
  test.describe(scheme, () => {
    test.use({ colorScheme: scheme });

    test("a task sequence", async ({ page }) => {
      await show(page, `/deployment/sequences/${flowSummary.id}?step=if1`, builderAnswers);
      await expect(page.getByRole("heading", { name: "Windows 11 office PCs" })).toBeVisible();
      await expect(
        page.getByRole("button", { name: /^Step 2, If: Is it a Latitude\?/ }),
      ).toBeVisible();

      await expect(page).toHaveScreenshot(`sequence-${scheme}.png`);
    });
  });
}

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

// The rules of an office in the order they are checked, with a Latitude in Berlin tested against them; and the accounts
// its steps use, one of them with a password this server cannot read.
const ruleAnswers = {
  "GET /api/rules": rules,
  "GET /api/machine-roles": roles,
  "GET /api/sequences": ruleSequences,
  "GET /api/sequences/facts": facts,
  "GET /api/machines": [testedMachine],
  [`GET /api/machines/${testedMachine.id}/sequence`]: testedResolution,
};

test.describe("tall", () => {
  test.use({ viewport: { width: 1440, height: 1080 } });

  test("rules", async ({ page }) => {
    await show(page, "/deployment/rules", ruleAnswers);
    await expect(page.getByRole("grid", { name: "Rules in order" })).toBeVisible();
    await page.getByRole("button", { name: /Show suggestions/ }).click();
    await page.getByRole("option", { name: /PC-G2341KXQ/ }).click();
    await expect(page.getByText("Windows 11 office PCs, from rule 4")).toBeVisible();
    await page.getByRole("heading", { name: "Rules", exact: true }).click();

    await expect(page).toHaveScreenshot("rules-light.png");
  });
});

test.describe("dark", () => {
  test.use({ colorScheme: "dark" });

  test("accounts", async ({ page }) => {
    await show(page, "/deployment/accounts", { "GET /api/accounts": accounts });
    await expect(page.getByRole("grid", { name: "Accounts" })).toBeVisible();

    await expect(page).toHaveScreenshot("accounts-dark.png");
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

  // On a phone the flow is its outline.
  test("a task sequence", async ({ page }) => {
    await show(page, `/deployment/sequences/${flowSummary.id}`, builderAnswers);
    await expect(
      page.getByRole("treegrid", { name: "Outline of Windows 11 office PCs" }),
    ).toBeVisible();

    await expect(page).toHaveScreenshot("sequence-phone.png");
  });
});
