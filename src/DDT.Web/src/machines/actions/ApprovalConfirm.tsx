// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { useState } from "react";

import { InputsDialog } from "@/inputs/InputsDialog";
import { machineLabel, type MachineSummary } from "@/machines/machines";
import { secureBootRisk, type SecureBootRisk } from "@/machines/secureBoot";
import type { MachineActionState } from "@/machines/useMachineActions";
import { Checkbox } from "@/ui/Checkbox";
import { ConfirmDialog } from "@/ui/ConfirmDialog";

import { approvalRequested } from "./machineRequests";

// Confirms an approval that runs the sequence a rule chose. Where that run writes a raw disk image that may not
// start with Secure Boot on, it says so and offers to allow the image; the server refuses the run without it where
// the machine said Secure Boot is on.
export function ApprovalConfirm({
  machine,
  actions,
}: {
  machine: MachineSummary;
  actions: MachineActionState;
}) {
  const [allowMismatch, setAllowMismatch] = useState(false);
  const plan = approvalRequested(actions.approveOn, machine);
  // From the machine as the list has it now, which may have registered again since the plan was made.
  const risk = plan === null ? null : secureBootRisk(machine, plan.sequence);
  const label = machineLabel(machine);

  // A sequence that asks something on the web asks it with the approval that runs it.
  if (plan !== null && plan.inputs.length > 0) {
    return (
      <InputsDialog
        title={<Trans>Approve {label}?</Trans>}
        inputs={plan.inputs}
        defaults={plan.defaults}
        confirmLabel={plan.confirmLabel}
        isBusy={actions.approveWithPlan.isPending}
        error={actions.approveWithPlan.error}
        isConfirmDisabled={risk?.required === true && !allowMismatch}
        onClose={() => {
          actions.setApproveOn(null);
          actions.approveWithPlan.reset();
          setAllowMismatch(false);
        }}
        onSubmit={(answers) => {
          actions.approveWithPlan.mutate({
            id: machine.id,
            plan,
            allowSecureBootMismatch: risk !== null && allowMismatch,
            answers,
          });
        }}
      >
        <p>{plan.consequence}</p>
        <SecureBootAllowance risk={risk} allowed={allowMismatch} onChange={setAllowMismatch} />
      </InputsDialog>
    );
  }

  return (
    <ConfirmDialog
      isOpen={plan !== null}
      onOpenChange={(open) => {
        if (!open) {
          actions.setApproveOn(null);
          setAllowMismatch(false);
        }
      }}
      title={<Trans>Approve {label}?</Trans>}
      confirmLabel={plan?.confirmLabel ?? ""}
      isBusy={actions.approveWithPlan.isPending}
      isConfirmDisabled={risk?.required === true && !allowMismatch}
      error={actions.approveWithPlan.isError ? actions.approveWithPlan.error.message : undefined}
      onConfirm={() => {
        if (plan !== null) {
          actions.approveWithPlan.mutate({
            id: machine.id,
            plan,
            allowSecureBootMismatch: risk !== null && allowMismatch,
          });
        }
      }}
    >
      <p>{plan?.consequence}</p>
      <SecureBootAllowance risk={risk} allowed={allowMismatch} onChange={setAllowMismatch} />
    </ConfirmDialog>
  );
}

function SecureBootAllowance({
  risk,
  allowed,
  onChange,
}: {
  risk: SecureBootRisk | null;
  allowed: boolean;
  onChange: (allowed: boolean) => void;
}) {
  return risk !== null ? (
    <>
      <p>{risk.warning}</p>
      <Checkbox isSelected={allowed} onChange={onChange}>
        {risk.allowLabel}
      </Checkbox>
    </>
  ) : null;
}
