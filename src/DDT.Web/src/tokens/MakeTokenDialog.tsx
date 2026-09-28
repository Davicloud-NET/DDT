// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import type { CurrentUser } from "@/auth/auth";
import { Button } from "@/ui/Button";
import { Dialog } from "@/ui/Dialog";

import { MakeTokenForm } from "./MakeTokenForm";
import { TokenSecret } from "./TokenSecret";
import { useMakeToken } from "./useMakeToken";

// Makes an API token for the signed-in user, with at most their own role. Shows the secret until the dialog closes.
export function MakeTokenDialog({ user, onClose }: { user: CurrentUser; onClose: () => void }) {
  const form = useMakeToken(user);
  const { created, make } = form;

  // Both states return the same Dialog, so the open dialog swaps its content instead of closing and opening again.
  if (created !== null) {
    const tokenName = created.token.name;

    return (
      <Dialog
        isOpen
        onOpenChange={(open) => {
          if (!open) {
            onClose();
          }
        }}
        title={<Trans>Copy the token {tokenName}</Trans>}
        footer={
          <Button variant="primary" onPress={onClose}>
            <Trans>Done</Trans>
          </Button>
        }
      >
        <TokenSecret created={created} />
      </Dialog>
    );
  }

  return (
    <Dialog
      isOpen
      onOpenChange={(open) => {
        if (!open) {
          onClose();
        }
      }}
      title={<Trans>Make an API token</Trans>}
      isBusy={make.isPending}
      footer={
        <>
          <Button variant="secondary" isDisabled={make.isPending} onPress={onClose}>
            <Trans>Cancel</Trans>
          </Button>
          <Button
            type="submit"
            form="make-token"
            variant="primary"
            isDisabled={make.isPending || !Number.isFinite(form.days)}
          >
            <Trans>Make token</Trans>
          </Button>
        </>
      }
    >
      <MakeTokenForm form={form} />
    </Dialog>
  );
}
