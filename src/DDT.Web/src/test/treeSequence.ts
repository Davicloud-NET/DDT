// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { SequenceDefinition, SequenceStep } from "@/sequences/sequences";

// The tree whose run treeRun holds: an IF on the model, a group for an office, a repeat and a pause.

export const node = {
  partition: "0193a4b2-0000-7000-8000-00000000b001",
  latitude: "0193a4b2-0000-7000-8000-00000000b002",
  applyLatitude: "0193a4b2-0000-7000-8000-00000000b003",
  drivers: "0193a4b2-0000-7000-8000-00000000b004",
  applyOther: "0193a4b2-0000-7000-8000-00000000b005",
  name: "0193a4b2-0000-7000-8000-00000000b006",
  answers: "0193a4b2-0000-7000-8000-00000000b007",
  join: "0193a4b2-0000-7000-8000-00000000b008",
  office: "0193a4b2-0000-7000-8000-00000000b009",
  share: "0193a4b2-0000-7000-8000-00000000b00a",
  printer: "0193a4b2-0000-7000-8000-00000000b00b",
  wait: "0193a4b2-0000-7000-8000-00000000b00c",
  test: "0193a4b2-0000-7000-8000-00000000b00d",
  pause: "0193a4b2-0000-7000-8000-00000000b00e",
  restart: "0193a4b2-0000-7000-8000-00000000b00f",
} as const;

const common = { conditions: [], continueOnError: false, rebootAfter: false };

function script(id: string, name: string, phase: "WindowsPE" | "Windows"): SequenceStep {
  return {
    ...common,
    id,
    name,
    kind: "runScript",
    phase,
    interpreter: "Cmd",
    script: "exit 0",
    packageId: null,
    timeoutMinutes: 60,
    successExitCodes: [0],
    rebootExitCodes: [3010],
  };
}

export const pauseMessage = "Stick the asset tag on the lid and note it in the inventory.";

export const treeDefinition: SequenceDefinition = {
  version: 3,
  steps: [
    {
      ...common,
      id: node.partition,
      name: "Partition the disk",
      kind: "partition",
      systemPartitionMegabytes: 300,
      recoveryPartitionMegabytes: 1024,
    },
    {
      ...common,
      id: node.latitude,
      name: "Is it a Latitude?",
      kind: "if",
      test: { kind: "test", variable: "Model", operator: "Contains", value: "Latitude" },
      then: [
        {
          ...common,
          id: node.applyLatitude,
          name: "Apply Windows 11 for Latitudes",
          kind: "applyImage",
          imageId: "0193a4b2-0000-7000-8000-0000000000a1",
        },
        {
          ...common,
          id: node.drivers,
          name: "Add the Latitude drivers",
          kind: "injectDrivers",
          requireMatch: true,
        },
      ],
      else: [
        {
          ...common,
          id: node.applyOther,
          name: "Apply Windows 11",
          kind: "applyImage",
          imageId: "0193a4b2-0000-7000-8000-0000000000a2",
        },
      ],
    },
    {
      ...common,
      id: node.name,
      name: "Name the computer",
      kind: "setVariable",
      variable: "ComputerName",
      value: "PC-{{SerialNumber|alnum|right:8}}",
    },
    {
      ...common,
      id: node.answers,
      name: "Write the answer file",
      kind: "writeUnattend",
      timeZone: "{{TimeZone}}",
      locale: null,
      keyboard: null,
      localAdministrator: false,
    },
    {
      ...common,
      id: node.join,
      name: "Join the domain",
      kind: "joinDomain",
      organizationalUnit: null,
    },
    {
      ...common,
      id: node.office,
      name: "Berlin office",
      kind: "group",
      when: { kind: "test", variable: "IPv4Address", operator: "InSubnet", value: "10.20.0.0/16" },
      steps: [
        script(node.share, "Map the site share", "Windows"),
        {
          ...script(node.printer, "Install the site printer", "Windows"),
          when: { kind: "test", variable: "DeviceKind", operator: "Equals", value: "Desktop" },
        },
      ],
    },
    {
      ...common,
      id: node.wait,
      name: "Wait for the share",
      kind: "repeat",
      until: { kind: "test", variable: "LastStepFailed", operator: "Equals", value: "false" },
      maxTimes: 5,
      goOnAtLimit: false,
      steps: [script(node.test, "Test the share", "Windows")],
    },
    {
      ...common,
      id: node.pause,
      name: "Check the asset tag",
      kind: "pause",
      message: pauseMessage,
      continueAfterMinutes: null,
    },
    { ...common, id: node.restart, name: "Restart", kind: "reboot" },
  ],
  inputs: [
    {
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
      askAt: "Both",
      account: null,
    },
  ],
};
