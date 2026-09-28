// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { ApprovalPlan } from "@/machines/approval";
import type { MachineSummary } from "@/machines/machines";

// The deployment the stop confirmation was opened for.
export interface StopRequest {
  machineId: string;
  deploymentId: string;
}

// The approval the confirmation was opened for.
export interface ApprovalRequest {
  machineId: string;
  plan: ApprovalPlan;
}

// The confirmation is for the deployment that was running when Stop was clicked. Once that one ended, the
// dialog closes, so a late confirm cannot stop another deployment on the same machine.
export function isStopRequested(stopOn: StopRequest | null, machine: MachineSummary): boolean {
  return (
    stopOn !== null &&
    stopOn.machineId === machine.id &&
    machine.deployment?.id === stopOn.deploymentId &&
    machine.deployment.state === "Running"
  );
}

// The plan holds while the machine waits. Once someone else decided, the dialog closes.
export function approvalRequested(
  approveOn: ApprovalRequest | null,
  machine: MachineSummary,
): ApprovalPlan | null {
  return approveOn !== null && approveOn.machineId === machine.id && machine.state === "Pending"
    ? approveOn.plan
    : null;
}
