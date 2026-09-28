// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { useLingui } from "@lingui/react/macro";

import type { StrayOffer } from "@/machines/list/strays";
import {
  isRemovable,
  machineLabel,
  type MachineState,
  type MachineSummary,
} from "@/machines/machines";
import type { MachineActionState } from "@/machines/useMachineActions";
import { MenuItem } from "@/ui/Menu";
import { RowActionsMenu } from "@/ui/RowActionsMenu";

// Reject is terminal. It is offered where a machine may be one to throw out; a running deployment is stopped
// instead.
const rejectableStates: readonly MachineState[] = ["Pending", "Approved", "Failed"];

interface MachineMenuProps {
  machine: MachineSummary;
  actions: MachineActionState;
  canAssign: boolean;
  strays: StrayOffer | null;
  layout: "row" | "panel";
}

// The menu beside the machine's key, with everything else that fits its state.
export function MachineMenu({ machine, actions, canAssign, strays, layout }: MachineMenuProps) {
  const { t: translate } = useLingui();
  const { decide, remove, cancel, busy } = actions;
  const deploymentState = machine.deployment?.state;
  const running = deploymentState === "Running" ? machine.deployment : null;
  const canApprove = machine.state === "Pending";
  const label = machineLabel(machine);

  const more = [
    ...(canApprove && canAssign
      ? [{ id: "assign", label: translate`Assign a sequence`, danger: false }]
      : []),
    ...(deploymentState === "Assigned"
      ? [{ id: "cancel", label: translate`Cancel the assignment`, danger: false }]
      : []),
    ...(running !== null && layout === "row"
      ? [{ id: "stop", label: translate`Stop the run`, danger: true }]
      : []),
    ...(rejectableStates.includes(machine.state)
      ? [{ id: "reject", label: translate`Reject`, danger: true }]
      : []),
    ...(isRemovable(machine) ? [{ id: "remove", label: translate`Remove`, danger: true }] : []),
    ...(strays !== null
      ? [
          {
            id: "strays",
            label: strayLabel(strays),
            danger: true,
          },
        ]
      : []),
  ];

  function act(id: string) {
    switch (id) {
      case "assign":
        actions.setAssignTo(machine.id);
        break;
      case "cancel":
        cancel.mutate(machine.id);
        break;
      case "stop":
        if (running !== null) {
          actions.stop.reset();
          actions.setStopOn({ machineId: machine.id, deploymentId: running.id });
        }
        break;
      case "reject":
        decide.mutate({ id: machine.id, approve: false });
        break;
      case "remove":
        remove.mutate({ id: machine.id });
        break;
      case "strays":
        if (strays !== null) {
          remove.mutate({ address: strays.address });
        }
        break;
    }
  }

  return more.length > 0 ? (
    <RowActionsMenu
      label={translate`Actions for ${label}`}
      triggerLabel={translate`More for ${label}`}
      isDisabled={busy}
      className="disabled:opacity-40"
      onAction={(key) => {
        act(String(key));
      }}
    >
      {more.map((item) => (
        <MenuItem key={item.id} id={item.id} className={item.danger ? "text-fail-text" : undefined}>
          {item.label}
        </MenuItem>
      ))}
    </RowActionsMenu>
  ) : null;
}

function strayLabel({ address, count }: StrayOffer): string {
  return t`Remove all ${count} waiting from ${address}`;
}
