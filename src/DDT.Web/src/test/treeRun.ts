// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type {
  DeploymentStepView,
  DeploymentSummary,
  DeploymentView,
} from "@/deployments/deployments";
import type { MachineSummary } from "@/machines/machines";

import { deploymentSummary, deploymentView, machineSummary, stepView } from "./builders";
import { node, pauseMessage, treeDefinition } from "./treeSequence";

// A run of treeDefinition, as the design canvas shows one: Then taken, a skipped step, a repeat gone round twice, and
// a pause it waits at since 10:02. Plain objects only, so the Playwright screens can use it as well.

export const treeMachineId = "0193a4b2-0000-7000-8000-000000000011";
export const treeRunId = "0193a4b2-0000-7000-8000-0000000000d7";

const at = (time: string) => `2026-09-16T${time}Z`;

function step(
  index: number,
  id: string,
  name: string,
  kind: string,
  more: Partial<DeploymentStepView> = {},
): DeploymentStepView {
  return stepView({ stepId: id, index, name, kind, pass: 0, ...more });
}

function done(from: string, to: string, more: Partial<DeploymentStepView> = {}) {
  return {
    state: "Done" as const,
    percent: 100,
    startedUtc: at(from),
    finishedUtc: at(to),
    pass: 1,
    ...more,
  };
}

export const treeSteps: DeploymentStepView[] = [
  step(0, node.partition, "Partition the disk", "partition", done("09:40:05", "09:40:53")),
  step(1, node.latitude, "Is it a Latitude?", "if", {
    ...done("09:40:53", "09:44:34"),
    branch: "Then",
    evaluation: [{ path: "test", held: true, actual: "Latitude 7450" }],
  }),
  step(2, node.applyLatitude, "Apply Windows 11 for Latitudes", "applyImage", {
    ...done("09:40:53", "09:43:53"),
    parentId: node.latitude,
    depth: 1,
  }),
  step(3, node.drivers, "Add the Latitude drivers", "injectDrivers", {
    ...done("09:43:53", "09:44:34"),
    parentId: node.latitude,
    depth: 1,
  }),
  step(4, node.applyOther, "Apply Windows 11", "applyImage", {
    state: "Skipped",
    finishedUtc: at("09:40:53"),
    parentId: node.latitude,
    depth: 1,
    pass: 1,
  }),
  step(5, node.name, "Name the computer", "setVariable", done("09:44:34", "09:44:34")),
  step(6, node.answers, "Write the answer file", "writeUnattend", done("09:44:34", "09:44:36")),
  step(7, node.join, "Join the domain", "joinDomain", {
    ...done("09:52:00", "09:52:14"),
    phase: "Windows",
  }),
  step(8, node.office, "Berlin office", "group", {
    ...done("09:52:14", "09:52:20"),
    phase: "Windows",
    evaluation: [{ path: "when", held: true, actual: "10.20.4.51" }],
  }),
  step(9, node.share, "Map the site share", "runScript", {
    ...done("09:52:14", "09:52:20"),
    phase: "Windows",
    parentId: node.office,
    depth: 1,
  }),
  step(10, node.printer, "Install the site printer", "runScript", {
    state: "Skipped",
    finishedUtc: at("09:52:20"),
    phase: "Windows",
    parentId: node.office,
    depth: 1,
    pass: 1,
    evaluation: [{ path: "when", held: false, actual: "Laptop" }],
  }),
  step(11, node.wait, "Wait for the share", "repeat", {
    ...done("09:52:20", "10:01:00"),
    phase: "Windows",
    iteration: 2,
    evaluation: [{ path: "until", held: true, actual: "false" }],
  }),
  step(12, node.test, "Test the share", "runScript", {
    ...done("10:00:00", "10:01:00", { pass: 2 }),
    phase: "Windows",
    parentId: node.wait,
    depth: 1,
  }),
  step(13, node.pause, "Check the asset tag", "pause", {
    state: "Running",
    startedUtc: at("10:02:00"),
    phase: "Windows",
    pass: 1,
  }),
  step(14, node.restart, "Restart", "reboot", { phase: "Windows" }),
];

export function treeRunSummary(overrides: Partial<DeploymentSummary> = {}): DeploymentSummary {
  return deploymentSummary({
    id: treeRunId,
    title: "Windows 11 office PCs",
    state: "Running",
    source: "Web",
    requestedBy: "anna",
    stepCount: 11,
    stepIndex: 9,
    stepName: "Check the asset tag",
    percent: 0,
    phase: "Windows",
    activity: "Paused",
    createdUtc: at("09:38:00"),
    startedUtc: at("09:40:00"),
    updatedUtc: at("10:02:00"),
    waiting: true,
    pauseMessage,
    ...overrides,
  });
}

export function treeRunView(overrides: Partial<DeploymentView> = {}): DeploymentView {
  return deploymentView({
    summary: treeRunSummary(),
    machineId: treeMachineId,
    sequenceRevision: 6,
    definition: treeDefinition,
    steps: treeSteps,
    values: [
      {
        name: "ComputerName",
        value: "PC-G2341KXQ",
        source: "Step",
        sourceId: node.name,
        sourceName: "Name the computer",
        overridden: false,
      },
      {
        name: "TimeZone",
        value: "W. Europe Standard Time",
        source: "Rule",
        sourceId: "0193a4b2-0000-7000-8000-0000000000f2",
        sourceName: "Berlin office",
        overridden: false,
      },
      {
        name: "OrganizationalUnit",
        value: "OU=Berlin,OU=Computers,DC=corp,DC=example",
        source: "Rule",
        sourceId: "0193a4b2-0000-7000-8000-0000000000f2",
        sourceName: "Berlin office",
        overridden: false,
      },
      {
        name: "TimeZone",
        value: "UTC",
        source: "DeploymentDefault",
        sourceId: null,
        sourceName: null,
        overridden: true,
      },
      {
        name: "Department",
        value: "Sales",
        source: "Input",
        sourceId: null,
        sourceName: null,
        overridden: false,
      },
      {
        name: "JoinAccount",
        value: null,
        source: "Input",
        sourceId: null,
        sourceName: null,
        overridden: false,
      },
    ],
    variables: { ComputerName: "PC-G2341KXQ" },
    inputs: [
      {
        input: {
          name: "Department",
          label: "Department",
          help: "Where the computer goes.",
          kind: "Choice",
          choices: [
            { value: "Sales", label: null },
            { value: "Finance", label: null },
            { value: "Service", label: "Customer service" },
          ],
          default: null,
          required: true,
          maxLength: null,
        },
        answered: true,
        answeredBy: "anna",
      },
    ],
    pause: {
      stepId: node.pause,
      pass: 1,
      message: pauseMessage,
      sinceUtc: at("10:02:00"),
      continuesUtc: null,
    },
    ...overrides,
  });
}

export function treeMachine(overrides: Partial<MachineSummary> = {}): MachineSummary {
  return machineSummary({
    id: treeMachineId,
    state: "Deploying",
    assignedName: "PC-G2341KXQ",
    manufacturer: "Dell Inc.",
    model: "Latitude 7450",
    serialNumber: "G2341KXQ",
    deviceKind: "Laptop",
    primaryMac: "A4BB6D1C2E3F",
    macAddresses: ["A4BB6D1C2E3F"],
    lastSeenAddress: "10.20.4.51",
    firstSeenUtc: "2026-09-16T09:30:00Z",
    lastSeenUtc: "2026-09-16T10:05:55Z",
    secureBootEnabled: true,
    deployment: treeRunSummary(),
    facts: {
      memoryMegabytes: 16384,
      processorName: "Intel(R) Core(TM) Ultra 7 165U",
      processorCores: 12,
      logicalProcessors: 14,
      tpmPresent: true,
      tpmVersion: "2.0",
      secureBootCapable: true,
      iPv4Address: "10.20.4.51",
      iPv4PrefixLength: 24,
      defaultGateway: "10.20.4.1",
      dnsSuffix: "berlin.corp.example",
      dhcpServer: "10.20.0.10",
      systemVersion: null,
      systemFamily: "Latitude",
      systemSku: "0C6B",
      assetTag: "INV-40211",
      baseboardProduct: "0R1XK3",
      biosVersion: "1.9.0",
      biosDate: "2026-06-11",
    },
    ...overrides,
  });
}
