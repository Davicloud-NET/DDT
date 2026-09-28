// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";

import type { TwoFactorEnrollment } from "@/auth/account";
import { Button } from "@/ui/Button";
import { QrCode } from "@/ui/QrCode";

import { AuthenticatorCodeField } from "./AuthenticatorCodeField";
import { groupKey } from "./authenticatorKey";

interface AuthenticatorEnrollmentProps {
  enrollment: TwoFactorEnrollment;
  code: string;
  onCodeChange: (code: string) => void;
  busy: boolean;
  onTurnOn: () => void;
  onCancel: () => void;
}

// Setting up an authenticator: its QR code or key, then a code it shows.
export function AuthenticatorEnrollment({
  enrollment,
  code,
  onCodeChange,
  busy,
  onTurnOn,
  onCancel,
}: AuthenticatorEnrollmentProps) {
  const { t } = useLingui();

  return (
    <form
      className="flex flex-col gap-3.5"
      onSubmit={(event) => {
        event.preventDefault();
        onTurnOn();
      }}
    >
      <p className="text-ink-2">
        <Trans>
          Scan the code with your authenticator app, or type the key into it. Then enter the code
          the app shows.
        </Trans>
      </p>
      <div className="flex flex-wrap items-center gap-5">
        <QrCode
          value={enrollment.authenticatorUri}
          label={t`QR code for your authenticator app`}
          className="size-44 shrink-0"
        />
        <div className="flex min-w-0 flex-col gap-1">
          <span className="type-small text-muted">
            <Trans>Key</Trans>
          </span>
          <span className="type-data break-all text-ink">{groupKey(enrollment.sharedKey)}</span>
        </div>
      </div>
      <AuthenticatorCodeField code={code} onChange={onCodeChange} />
      <div className="flex flex-wrap gap-2">
        <Button type="submit" variant="primary" isDisabled={busy || code.trim() === ""}>
          <Trans>Turn on</Trans>
        </Button>
        <Button isDisabled={busy} onPress={onCancel}>
          <Trans>Cancel</Trans>
        </Button>
      </div>
    </form>
  );
}
