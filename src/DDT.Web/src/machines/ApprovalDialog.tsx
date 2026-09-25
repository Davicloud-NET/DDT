// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useId, useState } from "react";

import { ConfirmDialog } from "@/components/ConfirmDialog";
import type { ApprovalPlan } from "@/machines/approval";
import { machineLabel, type MachineSummary } from "@/machines/machines";
import type { MachineActionState } from "@/machines/useMachineActions";

import styles from "./ApprovalDialog.module.scss";

export interface ApprovalDialogProps {
  machine: MachineSummary;
  plan: ApprovalPlan;
  actions: MachineActionState;
}

// Confirms an approval that runs the sequence a rule chose. Where that run writes a raw disk image that may not
// start with Secure Boot on, it says so and offers to allow the image; the server refuses the run without it where
// the machine said Secure Boot is on.
export function ApprovalDialog({ machine, plan, actions }: ApprovalDialogProps) {
  const { approveWithPlan } = actions;
  const allowId = useId();
  const [allowMismatch, setAllowMismatch] = useState(false);
  const risk = plan.secureBoot;

  return (
    <ConfirmDialog
      open
      onOpenChange={(open) => {
        if (!open) {
          actions.setApproveOn(null);
        }
      }}
      title={`Approve ${machineLabel(machine)}?`}
      consequence={plan.consequence}
      confirmLabel={plan.confirmLabel}
      busy={approveWithPlan.isPending}
      error={approveWithPlan.isError ? approveWithPlan.error.message : null}
      confirmDisabled={risk?.required === true && !allowMismatch}
      onConfirm={() => {
        approveWithPlan.mutate({
          id: machine.id,
          plan,
          allowSecureBootMismatch: risk !== null && allowMismatch,
        });
      }}
    >
      {risk !== null && (
        <div className={styles.secureBoot}>
          <p className={styles.warning}>{risk.warning}</p>
          <div className={styles.check}>
            <input
              id={allowId}
              type="checkbox"
              checked={allowMismatch}
              onChange={(event) => {
                setAllowMismatch(event.target.checked);
              }}
            />
            <label htmlFor={allowId}>{risk.allowLabel}</label>
          </div>
        </div>
      )}
    </ConfirmDialog>
  );
}
