// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";

import type { CurrentUser } from "@/auth/auth";
import { Button } from "@/ui/Button";
import { Notice } from "@/ui/Notice";
import { Panel } from "@/ui/Panel";
import { StateTag } from "@/ui/StateTag";

import { AuthenticatorEnrollment } from "./AuthenticatorEnrollment";
import { AuthenticatorOn } from "./AuthenticatorOn";
import { RecoveryCodesDialog } from "./RecoveryCodesDialog";
import { useAuthenticator } from "./useAuthenticator";

export function AuthenticatorPanel({ user }: { user: CurrentUser }) {
  const { t } = useLingui();
  const authenticator = useAuthenticator();
  const { enrollment, code, setCode, busy, error } = authenticator;

  return (
    <Panel
      title={<Trans>Authenticator</Trans>}
      actions={
        user.twoFactorEnabled ? (
          <StateTag tone="ok">{t`On`}</StateTag>
        ) : (
          <StateTag tone="idle">{t`Off`}</StateTag>
        )
      }
    >
      {user.twoFactorEnabled ? (
        <AuthenticatorOn
          code={code}
          onCodeChange={setCode}
          busy={busy}
          onTurnOff={authenticator.turnOff}
          onMakeRecoveryCodes={authenticator.makeRecoveryCodes}
        />
      ) : enrollment === null ? (
        <>
          <p className="text-ink-2">
            <Trans>
              A second factor protects this account even if its password is seen, for example while
              someone signs in at a machine being deployed.
            </Trans>
          </p>
          <div>
            <Button variant="primary" isDisabled={busy} onPress={authenticator.startEnrollment}>
              <Trans>Set up an authenticator</Trans>
            </Button>
          </div>
        </>
      ) : (
        <AuthenticatorEnrollment
          enrollment={enrollment}
          code={code}
          onCodeChange={setCode}
          busy={busy}
          onTurnOn={authenticator.turnOn}
          onCancel={authenticator.cancelEnrollment}
        />
      )}

      {error !== null ? <Notice tone="fail">{error.message}</Notice> : null}

      <RecoveryCodesDialog
        codes={authenticator.recoveryCodes}
        onClose={authenticator.closeRecoveryCodes}
      />
    </Panel>
  );
}
