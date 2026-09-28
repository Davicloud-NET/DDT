// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { CurrentUser } from "@/auth/auth";
import type {
  DeploymentOptionsView,
  DeploymentStepView,
  DeploymentSummary,
  DeploymentView,
} from "@/deployments/deployments";
import type { ImageSummary } from "@/images/images";
import type { MachineLogEntry } from "@/log/log";
import type { MachineSummary } from "@/machines/machines";
import type { PackageSummary } from "@/packages/packages";
import type { MachineRoleView } from "@/roles/roles";
import type { MachineSequenceResolution, RuleView } from "@/rules/rules";
import type { SequenceStep, SequenceSummary, SequenceView } from "@/sequences/sequences";

// Whole objects for tests, so a field the page reads is never missing from a fixture.

export function deploymentSummary(overrides: Partial<DeploymentSummary> = {}): DeploymentSummary {
  return {
    id: "0193a4b2-0000-7000-8000-0000000000d1",
    sequenceId: "0193a4b2-0000-7000-8000-0000000000e1",
    title: "Install Windows",
    state: "Assigned",
    source: "Web",
    requestedBy: "operator",
    stepCount: 5,
    stepIndex: null,
    stepName: null,
    percent: 0,
    phase: null,
    activity: null,
    createdUtc: "2026-09-16T10:00:00Z",
    startedUtc: null,
    finishedUtc: null,
    updatedUtc: "2026-09-16T10:00:00Z",
    error: null,
    ...overrides,
  };
}

export function machineSummary(overrides: Partial<MachineSummary> = {}): MachineSummary {
  return {
    id: "0193a4b2-0000-7000-8000-000000000001",
    state: "Approved",
    smbiosUuid: "44454c4c-5700-1038-8036-b7c04f5a344a",
    primaryMac: "00155D010203",
    macAddresses: ["00155D010203"],
    manufacturer: "Microsoft Corporation",
    model: "Virtual Machine",
    serialNumber: "1234",
    assignedName: null,
    agentVersion: "1.0.0",
    firstSeenUtc: "2026-09-16T09:00:00Z",
    lastSeenUtc: "2026-09-16T10:00:00Z",
    lastSeenAddress: "172.25.132.98",
    signedInBy: null,
    firstSeenAddress: "172.25.132.98",
    everApproved: true,
    disks: "Disk 0: Msft Virtual Disk, 64 GB, SCSI",
    eligibleDiskCount: 1,
    deployment: null,
    secureBootEnabled: null,
    trustedUefiCas: null,
    deviceKind: "Unknown",
    ...overrides,
  };
}

export function currentUser(role: "Administrator" | "Operator" | "Viewer"): CurrentUser {
  return {
    id: "0193a4b2-0000-7000-8000-00000000c001",
    userName: role.toLowerCase(),
    displayName: null,
    source: "Local",
    twoFactorEnabled: false,
    roles: [role],
    mustChangePassword: false,
  };
}

export const administrator = currentUser("Administrator");
export const operator = currentUser("Operator");
export const viewer = currentUser("Viewer");

export function sequenceSummary(overrides: Partial<SequenceSummary> = {}): SequenceSummary {
  return {
    id: "0193a4b2-0000-7000-8000-0000000000e1",
    name: "Install Windows",
    description: null,
    revision: 3,
    stepCount: 5,
    problemCount: 0,
    warningCount: 0,
    erasesDisk: true,
    needsComputerName: false,
    continuesInWindows: false,
    updatedUtc: "2026-09-15T10:00:00Z",
    updatedBy: "admin",
    rawImageName: null,
    rawImageBootCapability: null,
    rawImageSignedUnder: null,
    ...overrides,
  };
}

export function sequenceView(summary: SequenceSummary, steps: SequenceStep[]): SequenceView {
  return {
    id: summary.id,
    name: summary.name,
    description: summary.description,
    revision: summary.revision,
    definition: { version: 2, steps },
    stepPhases: steps.map(() => "WindowsPE"),
    problems: [],
    warnings: [],
    updatedUtc: summary.updatedUtc,
    updatedBy: summary.updatedBy,
  };
}

export function sequenceResolution(
  overrides: Partial<MachineSequenceResolution> = {},
): MachineSequenceResolution {
  return {
    source: "None",
    sequenceId: null,
    sequenceName: null,
    ruleId: null,
    problemCount: 0,
    explanation:
      "No rule matches the MAC addresses or the model of this machine, so an operator chooses its sequence.",
    ...overrides,
  };
}

export function deploymentOptions(
  overrides: Partial<DeploymentOptionsView> = {},
): DeploymentOptionsView {
  return {
    domainConfigured: false,
    requireWebApproval: false,
    zeroTouchEnabled: false,
    serverUtc: new Date().toISOString(),
    ...overrides,
  };
}

export function stepView(overrides: Partial<DeploymentStepView> = {}): DeploymentStepView {
  return {
    stepId: "s1",
    index: 0,
    name: "Partition",
    kind: "partition",
    phase: "WindowsPE",
    state: "Pending",
    percent: 0,
    startedUtc: null,
    finishedUtc: null,
    error: null,
    ...overrides,
  };
}

export function deploymentView(overrides: Partial<DeploymentView> = {}): DeploymentView {
  return {
    summary: deploymentSummary(),
    machineId: "0193a4b2-0000-7000-8000-000000000001",
    sequenceRevision: 3,
    ruleId: null,
    definition: { version: 2, steps: [] },
    steps: [],
    artifacts: [],
    allowSecureBootMismatch: false,
    ...overrides,
  };
}

export function imageSummary(overrides: Partial<ImageSummary> = {}): ImageSummary {
  return {
    id: "0193a4b2-0000-7000-8000-0000000000a1",
    name: "Windows 11 Pro",
    kind: "Wim",
    sha256: "0f1e2d3c4b5a69788796a5b4c3d2e1f00f1e2d3c4b5a69788796a5b4c3d2e1f0",
    sizeBytes: 2 * 1024 ** 3,
    wimIndex: 1,
    edition: "Professional",
    architecture: "x64",
    version: "10.0.26100.1",
    language: "en-US",
    installedBytes: 8 * 1024 ** 3,
    originalFileName: "install.wim",
    uploadedUtc: "2026-09-15T10:00:00Z",
    uploadedBy: "admin",
    bootCapability: null,
    bootDetail: null,
    sourceSha256: null,
    ...overrides,
  };
}

export function packageSummary(overrides: Partial<PackageSummary> = {}): PackageSummary {
  return {
    id: "0193a4b2-0000-7000-8000-0000000000b1",
    name: "Latitude drivers",
    kind: "Drivers",
    sha256: "00",
    sizeBytes: 300 * 1024 ** 2,
    expandedBytes: 900 * 1024 ** 2,
    fileCount: 14,
    targets: [{ manufacturer: "Dell Inc.", model: "Latitude*" }],
    description: null,
    originalFileName: "latitude.zip",
    uploadedUtc: "2026-09-15T10:00:00Z",
    bootImage: false,
    uploadedBy: "admin",
    ...overrides,
  };
}

// A rule at the top of the list that chooses Install Windows for Dell Latitudes.
export function ruleView(overrides: Partial<RuleView> = {}): RuleView {
  return {
    id: "0193a4b2-0000-7000-8000-0000000000f1",
    position: 0,
    name: "Latitude laptops",
    description: null,
    enabled: true,
    when: {
      kind: "all",
      parts: [
        { kind: "test", variable: "Manufacturer", operator: "Equals", value: "Dell Inc." },
        { kind: "test", variable: "Model", operator: "StartsWith", value: "Latitude" },
      ],
    },
    sequenceId: "0193a4b2-0000-7000-8000-0000000000e1",
    sequenceName: "Install Windows",
    values: [],
    roleIds: [],
    revision: 1,
    problems: [],
    matchingMachines: 0,
    updatedUtc: "2026-09-16T10:00:00Z",
    updatedBy: "admin",
    ...overrides,
  };
}

export function machineRole(overrides: Partial<MachineRoleView> = {}): MachineRoleView {
  return {
    id: "0193a4b2-0000-7000-8000-0000000000c1",
    name: "Office PC",
    description: null,
    values: [],
    revision: 1,
    ruleCount: 0,
    updatedUtc: "2026-09-16T10:00:00Z",
    updatedBy: "admin",
    ...overrides,
  };
}

// Line n arrives n seconds after 10:00 on the day of the fixtures, from run d1 and no step.
export function logLine(id: number, overrides: Partial<MachineLogEntry> = {}): MachineLogEntry {
  const time = new Date(Date.parse("2026-09-16T10:00:00Z") + id * 1000).toISOString();

  return {
    id,
    timestampUtc: time,
    receivedUtc: time,
    level: "Information",
    message: `Line ${String(id)}`,
    agentTimestampUtc: time,
    deploymentId: "d1",
    stepId: null,
    ...overrides,
  };
}
