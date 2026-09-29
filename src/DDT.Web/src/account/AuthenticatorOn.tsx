// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { Button } from "@/ui/Button";

import { AuthenticatorCodeField } from "./AuthenticatorCodeField";

interface AuthenticatorOnProps {
  code: string;
  onCodeChange: (code: string) => void;
  busy: boolean;
  onTurnOff: () => void;
  onMakeRecoveryCodes: () => void;
}

// The authenticator panel while the second factor is on.
export function AuthenticatorOn({
  code,
  onCodeChange,
  busy,
  onTurnOff,
  onMakeRecoveryCodes,
}: AuthenticatorOnProps) {
  return (
    <>
      <p className="text-ink-2">
        <Trans>
          Signing in asks for a code from your authenticator app. To turn it off, enter a current
          code.
        </Trans>
      </p>
      <AuthenticatorCodeField code={code} onChange={onCodeChange} />
      <div className="flex flex-wrap gap-2">
        <Button variant="danger" isDisabled={busy || code.trim() === ""} onPress={onTurnOff}>
          <Trans>Turn off</Trans>
        </Button>
        <Button isDisabled={busy} onPress={onMakeRecoveryCodes}>
          <Trans>Make new recovery codes</Trans>
        </Button>
      </div>
    </>
  );
}
