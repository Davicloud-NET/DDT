// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { Button } from "@/ui/Button";
import { Dialog } from "@/ui/Dialog";

import type { GuardedAction } from "../useGuardedAction";

// Confirms certificate.newRoot. Boot images pin DDT's root, so a pair from another root, or a new root,
// strands every netbooting machine until its boot image is built again.
export function NewRootDialog({
  action,
  generating,
}: {
  action: GuardedAction;
  generating: boolean;
}) {
  return (
    <Dialog
      isOpen={action.warnings !== null}
      onOpenChange={(next) => {
        if (!next) {
          action.cancelWarnings();
        }
      }}
      title={
        generating ? (
          <Trans>Make a new root?</Trans>
        ) : (
          <Trans>Install a certificate from another root?</Trans>
        )
      }
      footer={
        <>
          <Button onPress={action.cancelWarnings}>
            <Trans>Cancel</Trans>
          </Button>
          <Button variant="primary" onPress={action.confirmWarnings}>
            {generating ? <Trans>Make a new root</Trans> : <Trans>Install it anyway</Trans>}
          </Button>
        </>
      }
    >
      <p>
        {generating ? (
          <Trans>
            DDT has no root yet, so Generate makes one. Boot images pin the root, so every boot
            image has to be built again with the new one, and every browser that manages DDT has to
            trust it. Until then, netbooting machines cannot reach the server.
          </Trans>
        ) : (
          <Trans>
            This certificate does not come from DDT's root, which boot images pin. Every boot image
            has to be built again with its root, and every browser that manages DDT has to trust
            that root. Until then, netbooting machines cannot reach the server.
          </Trans>
        )}
      </p>
    </Dialog>
  );
}
