// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { Trans, useLingui } from "@lingui/react/macro";
import { IconDots } from "@tabler/icons-react";
import { useState } from "react";
import { Button as AriaButton, MenuTrigger } from "react-aria-components";

import { isActive } from "@/deployments/deployments";
import {
  isRemovable,
  machineLabel,
  type MachineState,
  type MachineSummary,
} from "@/machines/machines";
import { secureBootRisk } from "@/machines/secureBoot";
import {
  approvalRequested,
  isStopRequested,
  type MachineActionState,
} from "@/machines/useMachineActions";
import { Button } from "@/ui/Button";
import { Checkbox } from "@/ui/Checkbox";
import { ConfirmDialog } from "@/ui/Dialog";
import { Menu, MenuItem } from "@/ui/Menu";
import { Notice } from "@/ui/Notice";

import { AssignDialog } from "./AssignDialog";

// The server refuses an assignment while the machine deploys, and for good in these states.
const assignableStates: readonly MachineState[] = ["Pending", "Approved", "Done", "Failed"];

// Reject is terminal. It is offered where a machine may be one to throw out; a running deployment is stopped
// instead.
const rejectableStates: readonly MachineState[] = ["Pending", "Approved", "Failed"];

export interface MachineActionsProps {
  machine: MachineSummary;
  actions: MachineActionState;
  // Offers to remove every stray that registered from this address at once.
  strays?: { address: string; count: number } | null;
  // Where it sits: a row shows one key and a menu; the detail panel shows keys only.
  layout?: "row" | "panel";
}

// What an operator can do to one machine, and the dialogs those open. The key that matters most for the machine's
// state is shown; everything else is in the menu beside it.
export function MachineActions({
  machine,
  actions,
  strays = null,
  layout = "row",
}: MachineActionsProps) {
  const { t: translate } = useLingui();
  const { decide, prepareApproval, remove, cancel, busy } = actions;
  const deploymentState = machine.deployment?.state;
  const running = deploymentState === "Running" ? machine.deployment : null;
  const canApprove = machine.state === "Pending";
  const canAssign = assignableStates.includes(machine.state) && !isActive(machine.deployment);
  const size = layout === "row" ? "sm" : "md";
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

  return (
    <>
      <div className="flex items-center justify-end gap-1.5">
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
        {more.length > 0 ? (
          <MenuTrigger>
            <AriaButton
              aria-label={translate`More for ${label}`}
              isDisabled={busy}
              className="flex size-7.5 cursor-pointer items-center justify-center rounded-key text-muted key-motion outline-none hover:bg-hover pressed:bg-key-quiet-pressed hover:text-ink focus-visible:outline-2 focus-visible:outline-focus disabled:opacity-40"
            >
              <IconDots size={18} stroke={2} />
            </AriaButton>
            <Menu
              aria-label={translate`Actions for ${label}`}
              onAction={(key) => {
                act(String(key));
              }}
            >
              {more.map((item) => (
                <MenuItem
                  key={item.id}
                  id={item.id}
                  className={item.danger ? "text-fail-text" : undefined}
                >
                  {item.label}
                </MenuItem>
              ))}
            </Menu>
          </MenuTrigger>
        ) : null}
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
    </>
  );
}

// Why the last action on a machine failed, for the actions that open no dialog of their own.
export function MachineActionErrors({ actions }: { actions: MachineActionState }) {
  const failed = [actions.decide, actions.prepareApproval, actions.remove, actions.cancel].find(
    (mutation) => mutation.isError,
  );

  return failed?.error ? <Notice tone="fail">{failed.error.message}</Notice> : null;
}

// Confirms an approval that runs the sequence a rule chose. Where that run writes a raw disk image that may not
// start with Secure Boot on, it says so and offers to allow the image; the server refuses the run without it where
// the machine said Secure Boot is on.
function ApprovalConfirm({
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
      {risk !== null ? (
        <>
          <p>{risk.warning}</p>
          <Checkbox isSelected={allowMismatch} onChange={setAllowMismatch}>
            {risk.allowLabel}
          </Checkbox>
        </>
      ) : null}
    </ConfirmDialog>
  );
}

function strayLabel({ address, count }: { address: string; count: number }): string {
  return t`Remove all ${count} waiting from ${address}`;
}

// A run in Windows PE may have written part of the disk. In Windows the agent runs as a service that learns of the
// stop at its next contact with the server and removes itself, and Windows stays as far as it got.
function stopConsequence(machine: MachineSummary): string {
  const label = machineLabel(machine);

  return machine.deployment?.phase === "Windows"
    ? t`This stops the run on ${label}, which runs in its installed Windows. The agent there stops at its next contact with the server and removes itself; Windows stays installed as it is, with the steps done so far.`
    : t`This stops the run on ${label}. Its disk is left half written; assign a sequence again to deploy it.`;
}
