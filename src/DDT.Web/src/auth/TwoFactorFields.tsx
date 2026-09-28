// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { Button } from "@/ui/Button";
import { TextField } from "@/ui/TextField";

// The sign-in's second step: the authenticator's code, or one of the recovery codes instead.
export function TwoFactorFields({
  code,
  onCodeChange,
  useRecoveryCode,
  onToggleRecoveryCode,
}: {
  code: string;
  onCodeChange: (code: string) => void;
  useRecoveryCode: boolean;
  onToggleRecoveryCode: () => void;
}) {
  return (
    <>
      <TextField
        key={useRecoveryCode ? "recovery" : "code"}
        label={useRecoveryCode ? <Trans>Recovery code</Trans> : <Trans>Authenticator code</Trans>}
        hint={
          useRecoveryCode ? (
            <Trans>Enter one of the recovery codes you saved.</Trans>
          ) : (
            <Trans>Enter the six digits your authenticator app shows for DDT.</Trans>
          )
        }
        autoFocus
        inputMode={useRecoveryCode ? "text" : "numeric"}
        autoComplete="one-time-code"
        mono
        isRequired
        value={code}
        onChange={onCodeChange}
      />
      <Button variant="quiet" size="sm" className="self-start" onPress={onToggleRecoveryCode}>
        {useRecoveryCode ? (
          <Trans>Use an authenticator code</Trans>
        ) : (
          <Trans>Use a recovery code</Trans>
        )}
      </Button>
    </>
  );
}
