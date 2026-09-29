// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { useQueryClient } from "@tanstack/react-query";

import { Button } from "@/ui/Button";
import { Dialog } from "@/ui/Dialog";
import { Notice } from "@/ui/Notice";

import { generateCertificate, putCertificate, type CertificateView } from "../certificate";
import { useGuardedAction } from "../useGuardedAction";

import { CertificateProof } from "./CertificateProof";
import { NewRootDialog } from "./NewRootDialog";

// Issuing a certificate needs the password. Making a new root also needs a confirmation.
export function GenerateDialog({
  view,
  isOpen,
  onClose,
}: {
  view: CertificateView;
  isOpen: boolean;
  onClose: () => void;
}) {
  const queryClient = useQueryClient();
  const action = useGuardedAction({
    send: generateCertificate,
    onDone: (answer) => {
      putCertificate(queryClient, answer);
      onClose();
    },
  });

  const close = () => {
    action.reset();
    onClose();
  };

  return (
    <>
      <Dialog
        isOpen={isOpen}
        onOpenChange={(next) => {
          if (!next) {
            close();
          }
        }}
        title={<Trans>Generate a new certificate?</Trans>}
        isBusy={action.busy}
        footer={
          <>
            <Button onPress={close} isDisabled={action.busy}>
              <Trans>Cancel</Trans>
            </Button>
            <Button variant="primary" onPress={action.start} isDisabled={action.busy}>
              <Trans>Generate certificate</Trans>
            </Button>
          </>
        }
      >
        <p>
          <Trans>
            DDT issues a certificate from its root for localhost, this computer's name and
            addresses, and the server names below, and serves it at once, on trial until you keep
            it.
          </Trans>
        </p>
        {view.hasRoot ? null : (
          <p>
            <Trans>
              DDT has no root yet, so it makes one. Every boot image then has to be built again with
              it, and every browser that manages DDT has to trust it.
            </Trans>
          </p>
        )}
        {action.error === null ? null : <Notice tone="fail">{action.error}</Notice>}
      </Dialog>
      <CertificateProof action={action} confirmLabel={<Trans>Confirm and generate</Trans>} />
      <NewRootDialog action={action} generating />
    </>
  );
}
