// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { Button } from "@/ui/Button";
import { Dialog } from "@/ui/Dialog";

import { AddUserForm } from "./AddUserForm";
import { useAddUser } from "./useAddUser";
import type { CreatedUser } from "./users";

// Adds a local account. The server makes up its password, which the page shows once when this dialog has closed.
export function AddUserDialog({
  onClose,
  onCreated,
}: {
  onClose: () => void;
  onCreated: (created: CreatedUser) => void;
}) {
  const form = useAddUser(onCreated);
  const { create } = form;

  return (
    <Dialog
      isOpen
      onOpenChange={(open) => {
        if (!open) {
          onClose();
        }
      }}
      title={<Trans>Add a local account</Trans>}
      isBusy={create.isPending}
      footer={
        <>
          <Button variant="secondary" isDisabled={create.isPending} onPress={onClose}>
            <Trans>Cancel</Trans>
          </Button>
          <Button type="submit" form="add-user" variant="primary" isDisabled={create.isPending}>
            <Trans>Add account</Trans>
          </Button>
        </>
      }
    >
      <AddUserForm form={form} />
    </Dialog>
  );
}
