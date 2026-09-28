// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type {
  NodePhase,
  SequenceDefinition,
  SequenceProblem,
  SequenceStep,
} from "@/sequences/sequences";

// The design canvas's builder as a sequence: an IF on the model choosing the image, a computer name from a template,
// the join, a group for one office with a share, a repeat that tries a share until it answers, a pause and a restart.
// Plain objects, so the screenshots can use it outside the web client's build too.

const base = { conditions: [], continueOnError: false, rebootAfter: false };

export const windowsImageId = "0193a4b2-0000-7000-8000-0000000000a1";

const script = (id: string, name: string, extra: Partial<SequenceStep> = {}): SequenceStep =>
  ({
    ...base,
    id,
    name,
    kind: "runScript",
    phase: "Windows",
    interpreter: "PowerShell",
    script: "exit 0",
    packageId: null,
    timeoutMinutes: 60,
    successExitCodes: [0],
    rebootExitCodes: [3010],
    ...extra,
  }) as SequenceStep;

export const flowSteps: SequenceStep[] = [
  {
    ...base,
    id: "p",
    name: "Partition the disk",
    kind: "partition",
    systemPartitionMegabytes: 300,
    recoveryPartitionMegabytes: 1024,
  },
  {
    ...base,
    id: "if1",
    name: "Is it a Latitude?",
    kind: "if",
    test: { kind: "test", variable: "Model", operator: "Contains", value: "Latitude" },
    then: [
      {
        ...base,
        id: "a1",
        name: "Apply Windows 11 for Latitudes",
        kind: "applyImage",
        imageId: windowsImageId,
      },
      {
        ...base,
        id: "d1",
        name: "Add the Latitude drivers",
        kind: "injectDrivers",
        requireMatch: true,
      },
    ],
    else: [
      { ...base, id: "a2", name: "Apply Windows 11", kind: "applyImage", imageId: windowsImageId },
    ],
  },
  {
    ...base,
    id: "sv",
    name: "Name the computer",
    kind: "setVariable",
    variable: "ComputerName",
    value: "PC-{{SerialNumber|alnum|right:8}}",
  },
  {
    ...base,
    id: "ua",
    name: "Write the answer file",
    kind: "writeUnattend",
    timeZone: null,
    locale: null,
    keyboard: null,
    localAdministrator: true,
  },
  { ...base, id: "jd", name: "Join the domain", kind: "joinDomain", organizationalUnit: null },
  {
    ...base,
    id: "g1",
    name: "Berlin office",
    kind: "group",
    when: {
      kind: "all",
      parts: [
        { kind: "test", variable: "IPv4Address", operator: "InSubnet", value: "10.20.0.0/16" },
      ],
    },
    steps: [
      script("s1", "Map the site share", {
        shares: [
          { path: "\\\\fs01.berlin\\deploy", account: { accountId: null, input: "DeployShare" } },
        ],
      }),
      script("s2", "Install the site printer", {
        when: {
          kind: "all",
          parts: [{ kind: "test", variable: "DeviceKind", operator: "Equals", value: "Desktop" }],
        },
      }),
    ],
  },
  {
    ...base,
    id: "r1",
    name: "Wait for the share",
    kind: "repeat",
    until: { kind: "test", variable: "LastStepFailed", operator: "Equals", value: "false" },
    maxTimes: 5,
    goOnAtLimit: false,
    steps: [script("s3", "Test the share")],
  },
  {
    ...base,
    id: "pz",
    name: "Check the asset tag",
    kind: "pause",
    message: "Check the asset tag of {{ComputerName}}.",
    continueAfterMinutes: null,
  },
  { ...base, id: "rb", name: "Restart", kind: "reboot" },
] as SequenceStep[];

export const flowDefinition: SequenceDefinition = {
  version: 3,
  steps: flowSteps,
  variables: [
    {
      name: "ComputerName",
      default: null,
      description: "The name the machine joins the domain with.",
      setBySteps: true,
    },
    { name: "Office", default: "Standard", description: null, setBySteps: false },
  ],
  inputs: [
    {
      name: "DeployShare",
      label: "Account for the deploy share",
      help: null,
      kind: "Account",
      choices: [],
      default: null,
      required: true,
      maxLength: null,
      askAt: "Machine",
      account: { domain: null, hosts: ["fs01.berlin"], runAs: false },
    },
  ],
};

const windows = new Set(["jd", "g1", "s1", "s2", "r1", "s3", "pz", "rb"]);

export const flowPhases: NodePhase[] = [
  "p",
  "if1",
  "a1",
  "d1",
  "a2",
  "sv",
  "ua",
  "jd",
  "g1",
  "s1",
  "s2",
  "r1",
  "s3",
  "pz",
  "rb",
].map((nodeId) => ({ nodeId, phases: [windows.has(nodeId) ? "Windows" : "WindowsPE"] }));

export const flowProblems: SequenceProblem[] = [
  {
    stepId: "s1",
    field: "shares[0].path",
    message: "The host of a share must be a name the Account input DeployShare lists.",
  },
];
