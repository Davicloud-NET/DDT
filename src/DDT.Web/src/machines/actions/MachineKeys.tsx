// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import type { MachineSummary } from "@/machines/machines";
import type { MachineActionState } from "@/machines/useMachineActions";
import { Button } from "@/ui/Button";

interface MachineKeysProps {
  machine: MachineSummary;
  actions: MachineActionState;
  canAssign: boolean;
  layout: "row" | "panel";
}

// The main button for the machine's state. The detail panel also gets a button that stops the run.
export function MachineKeys({ machine, actions, canAssign, layout }: MachineKeysProps) {
  const { prepareApproval, busy } = actions;
  const deploymentState = machine.deployment?.state;
  const running = deploymentState === "Running" ? machine.deployment : null;
  const canApprove = machine.state === "Pending";
  const size = layout === "row" ? "sm" : "md";

  return (
    <>
      {canApprove ? (
        <Button
          size={size}
          variant={machine.signedInBy !== null ? "primary" : "secondary"}
          isDisabled={busy}
          onPress={() => {
            prepareApproval.mutate(machine);
          }}
        >
          <Trans>Approve</Trans>
        </Button>
      ) : canAssign ? (
        <Button
          size={size}
          isDisabled={busy}
          onPress={() => {
            actions.setAssignTo(machine.id);
          }}
        >
          <Trans>Assign</Trans>
        </Button>
      ) : null}
      {running !== null && layout === "panel" ? (
        <Button
          size={size}
          variant="danger"
          isDisabled={busy}
          onPress={() => {
            actions.stop.reset();
            actions.setStopOn({ machineId: machine.id, deploymentId: running.id });
          }}
        >
          <Trans>Stop the run</Trans>
        </Button>
      ) : null}
    </>
  );
}
