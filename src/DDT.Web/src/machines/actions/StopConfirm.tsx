// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";

import { machineLabel, type MachineSummary } from "@/machines/machines";
import type { MachineActionState } from "@/machines/useMachineActions";
import { ConfirmDialog } from "@/ui/ConfirmDialog";

import { isStopRequested } from "./machineRequests";

// Asks before stopping the run that Stop was clicked for, and says what the stop leaves behind.
export function StopConfirm({
  machine,
  actions,
}: {
  machine: MachineSummary;
  actions: MachineActionState;
}) {
  const label = machineLabel(machine);

  return (
    <ConfirmDialog
      isOpen={isStopRequested(actions.stopOn, machine)}
      onOpenChange={(open) => {
        if (!open) {
          actions.setStopOn(null);
        }
      }}
      title={<Trans>Stop the run on {label}?</Trans>}
      confirmLabel={<Trans>Stop the run</Trans>}
      danger
      isBusy={actions.stop.isPending}
      error={actions.stop.isError ? actions.stop.error.message : undefined}
      onConfirm={() => {
        actions.stop.mutate(machine.id);
      }}
    >
      <p>{stopConsequence(machine)}</p>
    </ConfirmDialog>
  );
}

// A run in a boot environment may have written part of the disk. In the installed system the agent learns of the
// stop at its next contact with the server and removes itself, and the system stays as far as the run got.
function stopConsequence(machine: MachineSummary): string {
  switch (machine.deployment?.phase) {
    case "Windows":
      return t`Windows stays as it is, with the steps done so far.`;
    case "Linux":
      return t`Linux stays as it is, with the steps done so far.`;
    default:
      return t`Its disk is left half written.`;
  }
}
