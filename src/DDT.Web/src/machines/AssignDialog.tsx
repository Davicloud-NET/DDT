// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { machineLabel, type MachineSummary } from "@/machines/machines";
import { Button } from "@/ui/Button";
import { Dialog } from "@/ui/Dialog";

import { AssignFields } from "./assign/AssignFields";
import { useAssignForm } from "./assign/useAssignForm";

// Assigns a task sequence. It first says what that does to the machine: whether its disk is erased and, for a
// waiting machine, whether the assignment also authorizes it. Typed passwords are forgotten when the dialog
// closes.
export function AssignDialog({
  machine,
  onClose,
}: {
  machine: MachineSummary;
  onClose: () => void;
}) {
  const form = useAssignForm(machine, onClose);
  const label = machineLabel(machine);
  const formId = `assign-${machine.id}`;

  const close = () => {
    form.answers.forgetPasswords();
    onClose();
  };

  return (
    <Dialog
      isOpen
      onOpenChange={(open) => {
        if (!open) {
          close();
        }
      }}
      title={<Trans>Assign a task sequence to {label}</Trans>}
      isBusy={form.assign.isPending}
      width="lg"
      footer={
        <>
          <Button variant="secondary" isDisabled={form.assign.isPending} onPress={close}>
            <Trans>Cancel</Trans>
          </Button>
          <Button type="submit" form={formId} variant="primary" isDisabled={!form.canSubmit}>
            <Trans>Assign sequence</Trans>
          </Button>
        </>
      }
    >
      <AssignFields id={formId} machine={machine} form={form} />
    </Dialog>
  );
}
