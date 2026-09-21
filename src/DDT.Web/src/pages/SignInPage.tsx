// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useQueryClient } from "@tanstack/react-query";
import { Link, useNavigate } from "@tanstack/react-router";
import { useState } from "react";

import { currentUserQuery, login, type LoginStatus } from "@/auth/auth";

import styles from "./SignInPage.module.scss";

type Step = "credentials" | "twoFactor";

export function SignInPage() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const [step, setStep] = useState<Step>("credentials");
  const [userName, setUserName] = useState("");
  const [password, setPassword] = useState("");
  const [code, setCode] = useState("");
  const [useRecoveryCode, setUseRecoveryCode] = useState(false);
  const [error, setError] = useState<string | null>(null);
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
        setError("This account is locked. Try again later or ask an administrator.");
        return;
      }

      if (status === "Failed") {
        setError(
          step === "twoFactor"
            ? "That code is not valid."
            : "That user name or password is not correct.",
        );
        return;
      }

      // The route guard reads this query with staleTime "static", so invalidating it is not
      // enough: the cached anonymous result has to be removed or the guard bounces straight back.
      queryClient.removeQueries({ queryKey: currentUserQuery.queryKey });
      await navigate({ to: "/" });
    } catch {
      setError("Could not reach the server.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className={styles.page}>
      <form
        className={styles.card}
        onSubmit={(event) => {
          event.preventDefault();
          void submit();
        }}
      >
        <h1 className={styles.title}>DDT</h1>

        {step === "credentials" ? (
          <>
            <label className={styles.field}>
              <span>User name</span>
              <input
                autoFocus
                autoComplete="username"
                value={userName}
                onChange={(event) => {
                  setUserName(event.target.value);
                }}
                required
              />
            </label>
            <label className={styles.field}>
              <span>Password</span>
              <input
                type="password"
                autoComplete="current-password"
                value={password}
                onChange={(event) => {
                  setPassword(event.target.value);
                }}
                required
              />
            </label>
          </>
        ) : (
          <>
            <p className={styles.hint}>
              {useRecoveryCode
                ? "Enter one of the recovery codes you saved."
                : "Enter the code from your authenticator app."}
            </p>
            <label className={styles.field}>
              <span>{useRecoveryCode ? "Recovery code" : "Authentication code"}</span>
              <input
                autoFocus
                inputMode={useRecoveryCode ? "text" : "numeric"}
                autoComplete="one-time-code"
                value={code}
                onChange={(event) => {
                  setCode(event.target.value);
                }}
                required
              />
            </label>
            <button
              type="button"
              className={styles.link}
              onClick={() => {
                setUseRecoveryCode(!useRecoveryCode);
                setCode("");
                setError(null);
              }}
            >
              {useRecoveryCode ? "Use an authenticator code" : "Use a recovery code"}
            </button>
          </>
        )}

        {error !== null && (
          <p className={styles.error} role="alert">
            {error}
          </p>
        )}

        <button type="submit" className={styles.submit} disabled={busy}>
          {busy ? "Signing in" : "Sign in"}
        </button>

        <Link to="/about" className={styles.about}>
          About DDT
        </Link>
      </form>
    </div>
  );
}
