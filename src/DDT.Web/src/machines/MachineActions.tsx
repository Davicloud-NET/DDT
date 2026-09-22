// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { ConfirmDialog } from "@/components/ConfirmDialog";
import { isActive } from "@/deployments/deployments";
import { AssignDialog } from "@/machines/AssignDialog";
import {
  isRemovable,
  machineLabel,
  type MachineState,
  type MachineSummary,
} from "@/machines/machines";
import {
  approvalRequested,
  isStopRequested,
  type MachineActionState,
} from "@/machines/useMachineActions";

import styles from "./MachineActions.module.scss";

// The server refuses an assignment while the machine deploys, and for good in these states.
const assignableStates: readonly MachineState[] = ["Pending", "Approved", "Done", "Failed"];

// Reject is terminal. It is offered where a machine may be one to throw out; a running deployment is
// stopped with Stop instead.
const rejectableStates: readonly MachineState[] = ["Pending", "Approved", "Failed"];

export interface MachineActionsProps {
  machine: MachineSummary;
  actions: MachineActionState;
  // Offers to remove every stray that registered from this address at once.
  strays?: { address: string; count: number } | null;
}

// The buttons for one machine, with the dialogs they open.
export function MachineActions({ machine, actions, strays = null }: MachineActionsProps) {
  const { decide, prepareApproval, approveWithPlan, remove, cancel, stop, busy } = actions;
  const approval = approvalRequested(actions.approveOn, machine);
  const deploymentState = machine.deployment?.state;
  const running = deploymentState === "Running" ? machine.deployment : null;

  return (
    <>
      <div className={styles.actions}>
        {machine.state === "Pending" && (
          <button
            type="button"
            className={styles.approve}
            disabled={busy}
            onClick={() => {
              prepareApproval.mutate(machine);
            }}
          >
            Approve
          </button>
        )}
        {assignableStates.includes(machine.state) && !isActive(machine.deployment) && (
          <button
            type="button"
            className={styles.approve}
            disabled={busy}
            onClick={() => {
              actions.setAssignTo(machine.id);
            }}
          >
            Assign
          </button>
        )}
        {deploymentState === "Assigned" && (
          <button
            type="button"
            className={styles.reject}
            disabled={busy}
            onClick={() => {
              cancel.mutate(machine.id);
            }}
          >
            Cancel
          </button>
        )}
        {running !== null && (
          <button
            type="button"
            className={styles.reject}
            disabled={busy}
            onClick={() => {
              stop.reset();
              actions.setStopOn({ machineId: machine.id, deploymentId: running.id });
            }}
          >
            Stop
          </button>
        )}
        {rejectableStates.includes(machine.state) && (
          <button
            type="button"
            className={styles.reject}
            disabled={busy}
            onClick={() => {
              decide.mutate({ id: machine.id, approve: false });
            }}
          >
            Reject
          </button>
        )}
        {isRemovable(machine) && (
          <button
            type="button"
            className={styles.reject}
            disabled={busy}
            onClick={() => {
              remove.mutate({ id: machine.id });
            }}
          >
            Remove
          </button>
        )}
        {strays !== null && (
          <button
            type="button"
            className={styles.reject}
            disabled={busy}
            onClick={() => {
              remove.mutate({ address: strays.address });
            }}
          >
            Remove all {strays.count} from {strays.address}
          </button>
        )}
      </div>

      {actions.assignTo === machine.id && (
        <AssignDialog
          machine={machine}
          onClose={() => {
            actions.setAssignTo(null);
          }}
        />
      )}

      {approval !== null && (
        <ConfirmDialog
          open
          onOpenChange={(open) => {
            if (!open) {
              actions.setApproveOn(null);
            }
          }}
          title={`Approve ${machineLabel(machine)}?`}
          consequence={approval.consequence}
          confirmLabel={approval.confirmLabel}
          busy={approveWithPlan.isPending}
          error={approveWithPlan.isError ? approveWithPlan.error.message : null}
          onConfirm={() => {
            approveWithPlan.mutate({ id: machine.id, plan: approval });
          }}
        />
      )}

      {isStopRequested(actions.stopOn, machine) && (
        <ConfirmDialog
          open
          onOpenChange={(open) => {
            if (!open) {
              actions.setStopOn(null);
            }
          }}
          title="Stop the deployment?"
          consequence={`This stops the deployment on ${machineLabel(machine)}. Its disk is left half written; assign a sequence again to deploy it.`}
          confirmLabel="Stop deployment"
          busy={stop.isPending}
          error={stop.isError ? stop.error.message : null}
          onConfirm={() => {
            stop.mutate(machine.id);
          }}
        />
      )}
    </>
  );
}
