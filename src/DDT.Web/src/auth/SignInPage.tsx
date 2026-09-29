// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import { useQuery } from "@tanstack/react-query";
import { Link, useSearch } from "@tanstack/react-router";
import { useState } from "react";
import { Form } from "react-aria-components";

import { StandaloneHeader } from "@/app/StandaloneHeader";
import { externalProvidersQuery, externalSignInUrl, type ExternalProvider } from "@/auth/auth";
import { Button } from "@/ui/Button";
import { buttonClass } from "@/ui/buttonClass";
import { Notice } from "@/ui/Notice";
import { TextField } from "@/ui/TextField";

import { TwoFactorFields } from "./TwoFactorFields";
import { useSignIn } from "./useSignIn";

export function SignInPage() {
  const { i18n, t } = useLingui();
  const signIn = useSignIn(useSearch({ from: "/sign-in" }));
  const step = signIn.step;
  const [userName, setUserName] = useState("");
  const [password, setPassword] = useState("");
  const [code, setCode] = useState("");
  const [useRecoveryCode, setUseRecoveryCode] = useState(false);
  // One button per single sign-on provider, while single sign-on is on. The code step doesn't show them.
  const providers = useQuery(externalProvidersQuery).data ?? [];

  return (
    <div className="flex min-h-full flex-col">
      <StandaloneHeader />
      <main className="flex flex-1 items-start justify-center px-4 pt-[12vh] pb-12">
        <Form
          className="flex w-full max-w-100 flex-col gap-5 rounded-panel bg-panel p-7 shadow-panel"
          onSubmit={(event) => {
            event.preventDefault();
            void signIn.submit({ userName, password, code, useRecoveryCode });
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
            <TwoFactorFields
              code={code}
              onCodeChange={setCode}
              useRecoveryCode={useRecoveryCode}
              onToggleRecoveryCode={() => {
                setUseRecoveryCode(!useRecoveryCode);
                setCode("");
                signIn.clearError();
              }}
            />
          )}

          {signIn.error ? <Notice tone="fail">{i18n._(signIn.error)}</Notice> : null}

          <Button type="submit" variant="primary" isDisabled={signIn.busy} className="w-full">
            {signIn.busy ? t`Signing in` : t`Sign in`}
          </Button>

          {step === "credentials" && providers.length > 0 ? (
            <ExternalSignIn providers={providers} />
          ) : null}

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

function ExternalSignIn({ providers }: { providers: ExternalProvider[] }) {
  return (
    <>
      <div className="flex items-center gap-3 type-small text-muted" aria-hidden="true">
        <span className="h-px flex-1 bg-line-soft" />
        <Trans>or</Trans>
        <span className="h-px flex-1 bg-line-soft" />
      </div>
      {/* A plain link, because the browser leaves for the provider and the app's router can't do that. */}
      {providers.map((provider) => {
        const name = provider.displayName;

        return (
          <a
            key={provider.scheme}
            href={externalSignInUrl(provider)}
            className={buttonClass("secondary", "md", "w-full")}
          >
            <Trans>Sign in with {name}</Trans>
          </a>
        );
      })}
    </>
  );
}
