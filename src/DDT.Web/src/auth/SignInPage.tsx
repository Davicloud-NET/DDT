// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { MessageDescriptor } from "@lingui/core";
import { msg } from "@lingui/core/macro";
import { Trans, useLingui } from "@lingui/react/macro";
import { useQueryClient } from "@tanstack/react-query";
import { Link, useNavigate, useSearch } from "@tanstack/react-router";
import { useState } from "react";
import { Form } from "react-aria-components";

import { StandaloneHeader } from "@/app/StandaloneHeader";
import { currentUserQuery, login, type LoginStatus } from "@/auth/auth";
import { Button } from "@/ui/Button";
import { Notice } from "@/ui/Notice";
import { TextField } from "@/ui/TextField";

type Step = "credentials" | "twoFactor";

const lockedMessage = msg`This account is locked. Try again later or ask an administrator.`;

// Why the server's OpenID Connect callback refused a sign-in.
const externalErrors: Record<string, MessageDescriptor> = {
  external: msg`The sign-in at the identity provider did not finish. Try again.`,
  unlinked: msg`No DDT account is linked to that identity. Ask an administrator.`,
  provision: msg`No account could be created for that identity. Ask an administrator.`,
  locked: lockedMessage,
  "not-allowed": msg`This account may not sign in. Ask an administrator.`,
};

export function SignInPage() {
  const { i18n, t } = useLingui();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const search = useSearch({ from: "/sign-in" });

  // After an OpenID Connect sign-in the server knows the account, and only its code is asked for.
  const [step, setStep] = useState<Step>(
    search.step === "two-factor" ? "twoFactor" : "credentials",
  );
  const [userName, setUserName] = useState("");
  const [password, setPassword] = useState("");
  const [code, setCode] = useState("");
  const [useRecoveryCode, setUseRecoveryCode] = useState(false);
  const [error, setError] = useState<MessageDescriptor | null>(
    search.error === undefined ? null : (externalErrors[search.error] ?? null),
  );
  const [busy, setBusy] = useState(false);

  async function submit() {
    setBusy(true);
    setError(null);

    try {
      const status: LoginStatus = await login({
        userName,
        password,
        ...(step === "twoFactor" && !useRecoveryCode ? { twoFactorCode: code } : {}),
        ...(step === "twoFactor" && useRecoveryCode ? { recoveryCode: code } : {}),
      });

      if (status === "RequiresTwoFactor") {
        setStep("twoFactor");
        return;
      }

      if (status === "LockedOut") {
        setError(lockedMessage);
        return;
      }

      if (status === "Failed") {
        setError(
          step === "twoFactor"
            ? msg`That code is not valid.`
            : msg`That user name or password is not correct.`,
        );
        return;
      }

      // The route guard reads this query with staleTime "static", so invalidating it is not enough: the cached
      // anonymous result has to be removed or the guard bounces straight back.
      queryClient.removeQueries({ queryKey: currentUserQuery.queryKey });
      await navigate({ to: "/machines" });
    } catch {
      setError(msg`Could not reach the server.`);
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="flex min-h-full flex-col">
      <StandaloneHeader />
      <main className="flex flex-1 items-start justify-center px-4 pt-[12vh] pb-12">
        <Form
          className="flex w-full max-w-100 flex-col gap-5 rounded-panel bg-panel p-7 shadow-panel"
          onSubmit={(event) => {
            event.preventDefault();
            void submit();
          }}
        >
          <h1 className="type-title">
            {step === "credentials" ? <Trans>Sign in</Trans> : <Trans>Confirm it is you</Trans>}
          </h1>

          {step === "credentials" ? (
            <>
              <TextField
                label={<Trans>User name</Trans>}
                autoFocus
                autoComplete="username"
                isRequired
                value={userName}
                onChange={setUserName}
              />
              <TextField
                label={<Trans>Password</Trans>}
                type="password"
                autoComplete="current-password"
                isRequired
                value={password}
                onChange={setPassword}
              />
            </>
          ) : (
            <>
              <TextField
                key={useRecoveryCode ? "recovery" : "code"}
                label={
                  useRecoveryCode ? <Trans>Recovery code</Trans> : <Trans>Authenticator code</Trans>
                }
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
                onChange={setCode}
              />
              <Button
                variant="quiet"
                size="sm"
                className="self-start"
                onPress={() => {
                  setUseRecoveryCode(!useRecoveryCode);
                  setCode("");
                  setError(null);
                }}
              >
                {useRecoveryCode ? (
                  <Trans>Use an authenticator code</Trans>
                ) : (
                  <Trans>Use a recovery code</Trans>
                )}
              </Button>
            </>
          )}

          {error ? <Notice tone="fail">{i18n._(error)}</Notice> : null}

          <Button type="submit" variant="primary" isDisabled={busy} className="w-full">
            {busy ? t`Signing in` : t`Sign in`}
          </Button>

          <Link
            to="/about"
            className="self-center type-small text-muted underline underline-offset-3 hover:text-ink"
          >
            <Trans>About DDT</Trans>
          </Link>
        </Form>
      </main>
    </div>
  );
}
