// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { MessageDescriptor } from "@lingui/core";
import { msg } from "@lingui/core/macro";
import { useQueryClient } from "@tanstack/react-query";
import { useNavigate } from "@tanstack/react-router";
import { useState } from "react";

import { currentUserQuery, login, type LoginStatus } from "./auth";

export type SignInStep = "credentials" | "twoFactor";

// What the person typed on the sign-in page.
export interface SignInEntry {
  userName: string;
  password: string;
  code: string;
  useRecoveryCode: boolean;
}

const lockedMessage = msg`This account is locked. Try again later or ask an administrator.`;
const unfinishedMessage = msg`The sign-in at the identity provider did not finish. Try again.`;

// Why the server's OpenID Connect callback refused a sign-in. A reason this page doesn't know yet shows as a sign-in
// that didn't finish.
const externalErrors: Record<string, MessageDescriptor> = {
  external: unfinishedMessage,
  unlinked: msg`No DDT account is linked to that identity. Ask an administrator.`,
  provision: msg`No account could be created for that identity. Ask an administrator.`,
  locked: lockedMessage,
  "not-allowed": msg`This account may not sign in. Ask an administrator.`,
  "no-role": msg`Your account is in none of the groups DDT maps to a role. Ask an administrator.`,
};

// Signs in with a password, then with a second factor if the account has one. search is the page's URL query.
export function useSignIn(search: { step?: "two-factor"; error?: string }) {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  // After an OpenID Connect sign-in the server already knows the account, so the page only asks for the code.
  const [step, setStep] = useState<SignInStep>(
    search.step === "two-factor" ? "twoFactor" : "credentials",
  );
  const [error, setError] = useState<MessageDescriptor | null>(
    search.error === undefined ? null : (externalErrors[search.error] ?? unfinishedMessage),
  );
  const [busy, setBusy] = useState(false);

  async function submit({ userName, password, code, useRecoveryCode }: SignInEntry) {
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

      if (status === "NoRole") {
        setError(
          msg`Your account is in none of the directory groups DDT gives a role to. Ask an administrator.`,
        );
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

  return {
    step,
    error,
    busy,
    submit,
    clearError: () => {
      setError(null);
    },
  };
}
