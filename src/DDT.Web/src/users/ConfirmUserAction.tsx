// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { ConfirmDialog } from "@/ui/ConfirmDialog";

import { UserActionConsequence } from "./UserActionConsequence";
import { shownName } from "./userView";
import type { Confirmation, ConfirmationRequest } from "./useUserActions";

interface ConfirmUserActionProps {
  request: ConfirmationRequest | null;
  isBusy: boolean;
  error: string | undefined;
  onCancel: () => void;
  onConfirm: () => void;
}

// Asks before an account is disabled, reset or deleted.
export function ConfirmUserAction({
  request,
  isBusy,
  error,
  onCancel,
  onConfirm,
}: ConfirmUserActionProps) {
  return (
    <ConfirmDialog
      isOpen={request !== null}
      onOpenChange={(open) => {
        if (!open) {
          onCancel();
        }
      }}
      title={request === null ? "" : <ConfirmTitle {...request} />}
      confirmLabel={request === null ? "" : <ConfirmLabel kind={request.kind} />}
      danger={request?.kind === "delete" || request?.kind === "disable"}
      isBusy={isBusy}
      error={error}
      onConfirm={onConfirm}
    >
      {request === null ? null : <UserActionConsequence {...request} />}
    </ConfirmDialog>
  );
}

function ConfirmTitle({ kind, user }: ConfirmationRequest) {
  const name = shownName(user);

  switch (kind) {
    case "disable":
      return <Trans>Disable {name}?</Trans>;
    case "reset-password":
      return <Trans>Give {name} a new password?</Trans>;
    case "reset-two-factor":
      return <Trans>Turn off the second factor of {name}?</Trans>;
    case "delete":
      return <Trans>Delete {name}?</Trans>;
  }
}

function ConfirmLabel({ kind }: { kind: Confirmation }) {
  switch (kind) {
    case "disable":
      return <Trans>Disable account</Trans>;
    case "reset-password":
      return <Trans>Make a new password</Trans>;
    case "reset-two-factor":
      return <Trans>Turn off second factor</Trans>;
    case "delete":
      return <Trans>Delete account</Trans>;
  }
}
