// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { useQuery } from "@tanstack/react-query";
import { useState } from "react";

import { deploymentOptionsQuery, type DeploymentOptionsView } from "@/deployments/deployments";
import { InputsDialog } from "@/inputs/InputsDialog";
import { startNotice, startsIn, type StartNotice } from "@/machines/bootEnvironment";
import { machineLabel, type MachineSummary } from "@/machines/machines";
import { linuxBootRefusal, secureBootRisk, type SecureBootRisk } from "@/machines/secureBoot";
import type { MachineActionState } from "@/machines/useMachineActions";
import type { SequenceSummary } from "@/sequences/sequences";
import { Checkbox } from "@/ui/Checkbox";
import { ConfirmDialog } from "@/ui/ConfirmDialog";
import { Notice } from "@/ui/Notice";

import { approvalRequested } from "./machineRequests";

// Confirms an approval that also runs the sequence a rule chose. If that run writes a raw disk image that may not
// boot with Secure Boot on, the dialog warns and offers to allow the image. If the machine reported Secure Boot
// on, the server refuses the run without that allowance.
export function ApprovalConfirm({
  machine,
  actions,
}: {
  machine: MachineSummary;
  actions: MachineActionState;
}) {
  const [allowMismatch, setAllowMismatch] = useState(false);
  const plan = approvalRequested(actions.approveOn, machine);
  // Uses the machine as the list has it now. It may have registered again since the plan was made.
  const risk = plan === null ? null : secureBootRisk(machine, plan.sequence);
  // Every row holds this dialog, so the server is only asked once one opens.
  const options = useQuery({ ...deploymentOptionsQuery, enabled: plan !== null });
  const start = plan === null ? null : startNote(machine, plan.sequence, options.data);
  const label = machineLabel(machine);

  // If the sequence asks for inputs on the web, the approval dialog asks for them.
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
        <StartNote start={start} />
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
      <StartNote start={start} />
      <SecureBootAllowance risk={risk} allowed={allowMismatch} onChange={setAllowMismatch} />
    </ConfirmDialog>
  );
}

// A machine whose firmware would refuse the Linux boot environment is not restarted into it, and is told why instead.
function startNote(
  machine: MachineSummary,
  sequence: SequenceSummary | null,
  options: DeploymentOptionsView | undefined,
): StartNotice | null {
  const refused =
    sequence !== null && startsIn(sequence) === "LinuxBoot"
      ? linuxBootRefusal(machine, options?.linuxShimSignedUnder, options?.bootEnvironments)
      : null;

  return refused === null
    ? startNotice(machine, sequence, options?.bootEnvironments)
    : { warning: true, text: refused };
}

// Where the run starts when that is not where the machine waits, as the assign dialog says it.
function StartNote({ start }: { start: StartNotice | null }) {
  return start === null ? null : start.warning ? (
    <Notice tone="attention">{start.text}</Notice>
  ) : (
    <p>{start.text}</p>
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
