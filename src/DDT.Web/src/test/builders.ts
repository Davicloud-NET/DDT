// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { DeploymentSummary } from "@/deployments/deployments";
import type { MachineSummary } from "@/machines/machines";

// Whole summaries for tests, so a field the page reads is never missing from a fixture.

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
    ...overrides,
  };
}
