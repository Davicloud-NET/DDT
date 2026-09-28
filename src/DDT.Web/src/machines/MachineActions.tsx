// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { isActive } from "@/deployments/deployments";
import type { MachineState, MachineSummary } from "@/machines/machines";
import type { MachineActionState } from "@/machines/useMachineActions";

import { ApprovalConfirm } from "./actions/ApprovalConfirm";
import { MachineKeys } from "./actions/MachineKeys";
import { MachineMenu } from "./actions/MachineMenu";
import { StopConfirm } from "./actions/StopConfirm";
import { AssignDialog } from "./AssignDialog";
import type { StrayOffer } from "./list/strays";

// Only these states take an assignment. The server refuses one while the machine deploys, and for good once it's
// rejected or retired.
const assignableStates: readonly MachineState[] = ["Pending", "Approved", "Done", "Failed"];

export interface MachineActionsProps {
  machine: MachineSummary;
  actions: MachineActionState;
  // Offers to remove every stray that registered from this address at once.
  strays?: StrayOffer | null;
  // A row shows one button and a menu. The detail panel shows its buttons, and a menu without Stop.
  layout?: "row" | "panel";
}

// What an operator can do to one machine, and the dialogs those actions open. The main button for the machine's
// state is shown, and everything else is in the menu next to it.
export function MachineActions({
  machine,
  actions,
  strays = null,
  layout = "row",
}: MachineActionsProps) {
  const canAssign = assignableStates.includes(machine.state) && !isActive(machine.deployment);

  return (
    <>
      <div className="flex items-center justify-end gap-1.5">
        <MachineKeys machine={machine} actions={actions} canAssign={canAssign} layout={layout} />
        <MachineMenu
          machine={machine}
          actions={actions}
          canAssign={canAssign}
          strays={strays}
          layout={layout}
        />
      </div>

      {actions.assignTo === machine.id ? (
        <AssignDialog
          machine={machine}
          onClose={() => {
            actions.setAssignTo(null);
          }}
        />
      ) : null}

      <ApprovalConfirm machine={machine} actions={actions} />

      <StopConfirm machine={machine} actions={actions} />
    </>
  );
}
