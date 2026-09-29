// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { useId, type ReactNode } from "react";

import { Button } from "@/ui/Button";
import { Dialog } from "@/ui/Dialog";
import { Notice } from "@/ui/Notice";
import { TextField } from "@/ui/TextField";

import { useReauthForm } from "./useReauthForm";

// Fields that grant roles or trust, and actions such as the agent upload, need the password again. The server's
// token lasts a few minutes, so several saves in a row ask only once. onAccepted resends the refused request.
export function ReauthDialog({
  isOpen,
  onAccepted,
  onCancel,
  reason,
  confirmLabel,
}: {
  isOpen: boolean;
  onAccepted: () => void;
  onCancel: () => void;
  // Why the password is needed, when it's not for a section's save.
  reason?: ReactNode;
  confirmLabel?: ReactNode;
}) {
  const formId = useId();
  const reauth = useReauthForm(onAccepted, onCancel);

  return (
    <Dialog
      isOpen={isOpen}
      onOpenChange={(open) => {
        if (!open) {
          reauth.close();
        }
      }}
      title={<Trans>Confirm it is you</Trans>}
      isBusy={reauth.busy}
      footer={
        <>
          <Button onPress={reauth.close} isDisabled={reauth.busy}>
            <Trans>Cancel</Trans>
          </Button>
          <Button
            type="submit"
            form={formId}
            variant="primary"
            isDisabled={reauth.busy || reauth.password === ""}
          >
            {confirmLabel ?? <Trans>Confirm and save</Trans>}
          </Button>
        </>
      }
    >
      <form
        id={formId}
        className="flex flex-col gap-3"
        onSubmit={(event) => {
          event.preventDefault();
          reauth.submit();
        }}
      >
        <p>
          {reason ?? (
            <Trans>
              These settings decide who signs in and what machines trust, so they need your password
              again.
            </Trans>
          )}
        </p>
        <TextField
          label={<Trans>Password</Trans>}
          type="password"
          autoComplete="current-password"
          value={reauth.password}
          onChange={reauth.setPassword}
          autoFocus
        />
        <TextField
          label={<Trans>Authenticator code, if you use one</Trans>}
          inputMode="numeric"
          autoComplete="one-time-code"
          mono
          value={reauth.code}
          onChange={reauth.setCode}
          className="max-w-60"
        />
        {reauth.error !== null ? <Notice tone="fail">{reauth.error}</Notice> : null}
      </form>
    </Dialog>
  );
}
