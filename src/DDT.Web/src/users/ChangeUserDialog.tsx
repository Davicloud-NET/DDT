// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { Button } from "@/ui/Button";
import { Dialog } from "@/ui/Dialog";

import { ChangeUserForm } from "./ChangeUserForm";
import { useChangeUser } from "./useChangeUser";
import type { UserView } from "./users";
import { shownName } from "./userView";

// Changes an account's name, email address and role. Whatever the directory or groups decide is locked, with the
// reason. So is your own role, because the server refuses to take the Administrator role from the person asking.
export function ChangeUserDialog({
  user,
  isSelf,
  onClose,
}: {
  user: UserView;
  isSelf: boolean;
  onClose: () => void;
}) {
  const form = useChangeUser({ user, isSelf, onClose });
  const { save } = form;
  const name = shownName(user);

  return (
    <Dialog
      isOpen
      onOpenChange={(open) => {
        if (!open) {
          onClose();
        }
      }}
      title={<Trans>Change {name}</Trans>}
      isBusy={save.isPending}
      footer={
        <>
          <Button variant="secondary" isDisabled={save.isPending} onPress={onClose}>
            <Trans>Cancel</Trans>
          </Button>
          <Button
            type="submit"
            form={form.formId}
            variant="primary"
            isDisabled={save.isPending || !form.changed}
          >
            <Trans>Save changes</Trans>
          </Button>
        </>
      }
    >
      <ChangeUserForm user={user} form={form} />
    </Dialog>
  );
}
