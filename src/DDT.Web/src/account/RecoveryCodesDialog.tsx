// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { Button } from "@/ui/Button";
import { CopyButton } from "@/ui/CopyButton";
import { Dialog } from "@/ui/Dialog";

// Shows new recovery codes once. Closing the dialog forgets them.
export function RecoveryCodesDialog({
  codes,
  onClose,
}: {
  codes: string[] | null;
  onClose: () => void;
}) {
  return (
    <Dialog
      isOpen={codes !== null}
      onOpenChange={(open) => {
        if (!open) {
          onClose();
        }
      }}
      title={<Trans>Your recovery codes</Trans>}
      footer={
        <>
          <CopyButton text={(codes ?? []).join("\n")}>
            <Trans>Copy</Trans>
          </CopyButton>
          <Button variant="primary" onPress={onClose}>
            <Trans>I have saved them</Trans>
          </Button>
        </>
      }
    >
      <p>
        <Trans>
          Each code signs you in once if you lose the authenticator. Keep them somewhere safe: they
          are shown only now, and new ones replace these.
        </Trans>
      </p>
      <ul className="grid grid-cols-2 gap-x-6 gap-y-1.5 rounded-key bg-well p-4 type-data text-ink">
        {(codes ?? []).map((recoveryCode) => (
          <li key={recoveryCode}>{recoveryCode}</li>
        ))}
      </ul>
    </Dialog>
  );
}
