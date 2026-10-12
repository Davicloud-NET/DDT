// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { DeploymentView } from "@/deployments/deployments";
import type { MachineLogEntry } from "@/log/log";
import type { MachineSummary } from "@/machines/machines";
import type { SequenceDefinition } from "@/sequences/sequences";

import { deploymentSummary, deploymentView, logLine, machineSummary, stepView } from "./builders";

// A run of the Install Linux template whose installed Linux never called: handed over at 09:47:50, and at 09:54 the
// machine started from the network again and was sent back to its disk. Plain objects, for the Playwright screens too.

export const linuxMachineId = "0193a4b2-0000-7000-8000-000000000021";
export const linuxRunId = "0193a4b2-0000-7000-8000-0000000000d9";

const step = {
  image: "0193a4b2-0000-7000-8000-00000000c001",
  seed: "0193a4b2-0000-7000-8000-00000000c002",
  script: "0193a4b2-0000-7000-8000-00000000c003",
} as const;

const at = (time: string) => `2026-09-16T${time}Z`;
const common = { conditions: [], continueOnError: false, rebootAfter: false };

const definition: SequenceDefinition = {
  version: 5,
  steps: [
    {
      ...common,
      id: step.image,
      name: "Write the disk image",
      kind: "writeRawImage",
      imageId: "0193a4b2-0000-7000-8000-0000000000a9",
    },
    {
      ...common,
      id: step.seed,
      name: "Write the cloud-init seed",
      kind: "writeCloudInitSeed",
      metaData: 'instance-id: "{{SmbiosUuid}}"\nlocal-hostname: "{{ComputerName}}"\n',
      userData: "#cloud-config\nusers:\n  - default\n",
      networkConfig: null,
    },
    {
      ...common,
      id: step.script,
      name: "Say where the run goes on",
      kind: "runScript",
      phase: "Linux",
      interpreter: "Shell",
      script: 'echo "The run goes on in $(uname -sr) on $(hostname), as $(id -un)."\n',
      packageId: null,
      timeoutMinutes: 60,
      successExitCodes: [0],
      rebootExitCodes: [194],
    },
  ],
};

const summary = deploymentSummary({
  id: linuxRunId,
  title: "Install Linux",
  state: "Running",
  requestedBy: "anna",
  stepCount: 3,
  stepIndex: 1,
  stepName: "Write the cloud-init seed",
  percent: 0,
  phase: "Linux",
  activity: "Restarting",
  createdUtc: at("09:44:00"),
  startedUtc: at("09:45:00"),
  updatedUtc: at("09:54:00"),
});

export function linuxRunView(): DeploymentView {
  return deploymentView({
    summary,
    machineId: linuxMachineId,
    sequenceRevision: 2,
    definition,
    steps: [
      stepView({
        stepId: step.image,
        index: 0,
        name: "Write the disk image",
        kind: "writeRawImage",
        phase: "LinuxBoot",
        state: "Done",
        percent: 100,
        startedUtc: at("09:45:05"),
        finishedUtc: at("09:47:40"),
      }),
      stepView({
        stepId: step.seed,
        index: 1,
        name: "Write the cloud-init seed",
        kind: "writeCloudInitSeed",
        phase: "LinuxBoot",
        state: "Done",
        percent: 100,
        startedUtc: at("09:47:40"),
        finishedUtc: at("09:47:42"),
      }),
      stepView({
        stepId: step.script,
        index: 2,
        name: "Say where the run goes on",
        kind: "runScript",
        phase: "Linux",
      }),
    ],
  });
}

// The machine as the list has it: still in the boot environment as far as the server knows, back there once.
export function linuxMachine(): MachineSummary {
  return machineSummary({
    id: linuxMachineId,
    state: "Deploying",
    assignedName: "SRV-LAB-07",
    manufacturer: "Dell Inc.",
    model: "PowerEdge R360",
    serialNumber: "7QK2M04",
    deviceKind: "Server",
    primaryMac: "D0946652A1C8",
    macAddresses: ["D0946652A1C8"],
    lastSeenAddress: "10.0.8.27",
    firstSeenAddress: "10.0.8.27",
    firstSeenUtc: at("09:41:00"),
    lastSeenUtc: at("09:54:00"),
    disks: "Disk 0: Dell BOSS-N1, 480 GB, NVMe",
    secureBootEnabled: true,
    agentEnvironment: "LinuxBoot",
    sequenceVersion: 5,
    handedOverUtc: at("09:54:00"),
    bootReturns: 1,
    deployment: summary,
  });
}

const lines: { message: string; stepId: string | null; time: string }[] = [
  { message: "Step Write the disk image begins.", stepId: step.image, time: "09:45:05" },
  { message: "Step Write the cloud-init seed begins.", stepId: step.seed, time: "09:47:40" },
  { message: "The machine restarts into the installed Linux.", stepId: null, time: "09:47:50" },
];

export const linuxRunLines: MachineLogEntry[] = lines.map(({ message, stepId, time }, index) =>
  logLine(index + 1, {
    message,
    stepId,
    deploymentId: linuxRunId,
    timestampUtc: at(time),
    receivedUtc: at(time),
    agentTimestampUtc: at(time),
  }),
);
