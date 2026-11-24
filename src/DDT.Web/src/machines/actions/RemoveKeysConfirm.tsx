// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { plural } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";

import { machineLabel, type MachineSummary } from "@/machines/machines";
import type { MachineActionState } from "@/machines/useMachineActions";
import { ConfirmDialog } from "@/ui/ConfirmDialog";

import { recoveryKeysToConfirm } from "./removalKeys";

// Asks before a machine goes with its recovery keys, naming how many the server said it has.
export function RemoveKeysConfirm({
  machine,
  actions,
}: {
  machine: MachineSummary;
  actions: MachineActionState;
}) {
  const label = machineLabel(machine);
  const request = actions.removeKeysOn?.machineId === machine.id ? actions.removeKeysOn : null;
  const count = request?.count ?? 0;

  return (
    <ConfirmDialog
      isOpen={request !== null}
      onOpenChange={(open) => {
        if (!open) {
          actions.setRemoveKeysOn(null);
          actions.remove.reset();
        }
      }}
      title={<Trans>Remove {label} and its recovery keys?</Trans>}
      confirmLabel={<Trans>Remove</Trans>}
      danger
      isBusy={actions.remove.isPending}
      error={
        actions.remove.isError && recoveryKeysToConfirm(actions.remove.error) === null
          ? actions.remove.error.message
          : undefined
      }
      onConfirm={() => {
        actions.remove.mutate({ id: machine.id, recoveryKeys: count });
      }}
    >
      <p>
        {plural(count, {
          one: "DDT holds # recovery key of this machine. It goes with the machine and cannot be revealed again.",
          other:
            "DDT holds # recovery keys of this machine. They go with the machine and cannot be revealed again.",
        })}
      </p>
    </ConfirmDialog>
  );
}
