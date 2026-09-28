// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { Button } from "@/ui/Button";
import { Dialog } from "@/ui/Dialog";
import { Notice } from "@/ui/Notice";
import { SecretValue } from "@/ui/SecretValue";

export interface ShownPassword {
  name: string;
  password: string;
  // Made by a reset, not for a new account.
  reset: boolean;
}

// The password the server made up for a new account or a reset, shown once. Closing the dialog forgets it.
export function PasswordDialog({
  shown,
  onClose,
}: {
  shown: ShownPassword | null;
  onClose: () => void;
}) {
  const name = shown?.name ?? "";

  return (
    <Dialog
      isOpen={shown !== null}
      onOpenChange={(open) => {
        if (!open) {
          onClose();
        }
      }}
      title={
        shown?.reset === true ? (
          <Trans>New password for {name}</Trans>
        ) : (
          <Trans>Password for {name}</Trans>
        )
      }
      footer={
        <Button variant="primary" onPress={onClose}>
          <Trans>Done</Trans>
        </Button>
      }
    >
      {shown?.reset === true ? (
        <p>
          <Trans>
            The old password no longer works, and {name} is signed out within a minute. Hand over
            this one in person or through a channel you trust.
          </Trans>
        </p>
      ) : (
        <p>
          <Trans>
            The account is ready. Hand over this password in person or through a channel you trust.
          </Trans>
        </p>
      )}
      <SecretValue label={<Trans>One-time password</Trans>} value={shown?.password ?? ""} />
      <Notice tone="attention">
        <Trans>
          DDT shows this password only now. At the next sign-in, {name} has to set a password of
          their own, and can do nothing else until then.
        </Trans>
      </Notice>
    </Dialog>
  );
}
