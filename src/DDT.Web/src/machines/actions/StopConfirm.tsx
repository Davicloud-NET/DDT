// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";

import { machineLabel, type MachineSummary } from "@/machines/machines";
import type { MachineActionState } from "@/machines/useMachineActions";
import { ConfirmDialog } from "@/ui/ConfirmDialog";

import { isStopRequested } from "./machineRequests";

// Confirms stopping the run Stop was clicked for, and says what that leaves behind.
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

// A run in Windows PE may have written part of the disk. In Windows the agent runs as a service that learns of the
// stop at its next contact with the server and removes itself, and Windows stays as far as it got.
function stopConsequence(machine: MachineSummary): string {
  const label = machineLabel(machine);

  return machine.deployment?.phase === "Windows"
    ? t`This stops the run on ${label}, which runs in its installed Windows. The agent there stops at its next contact with the server and removes itself; Windows stays installed as it is, with the steps done so far.`
    : t`This stops the run on ${label}. Its disk is left half written; assign a sequence again to deploy it.`;
}
