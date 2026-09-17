import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";

import {
  changePassword,
  disableTwoFactor,
  enableTwoFactor,
  regenerateRecoveryCodes,
  startTwoFactorEnrollment,
  type TwoFactorEnrollment,
} from "@/auth/account";
import { currentUserQuery } from "@/auth/auth";

import styles from "./AccountPage.module.scss";

export function AccountPage() {
  const queryClient = useQueryClient();
  const user = useQuery(currentUserQuery).data ?? null;

  const [currentPassword, setCurrentPassword] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [repeatedPassword, setRepeatedPassword] = useState("");
  const [passwordError, setPasswordError] = useState<string | null>(null);
  const [passwordChanged, setPasswordChanged] = useState(false);

  const [enrollment, setEnrollment] = useState<TwoFactorEnrollment | null>(null);
  const [code, setCode] = useState("");
  const [codeError, setCodeError] = useState<string | null>(null);
  const [recoveryCodes, setRecoveryCodes] = useState<string[] | null>(null);
  const [busy, setBusy] = useState(false);

  const fromDirectory = user?.source === "Directory";

  async function submitPassword() {
    setPasswordError(null);
    setPasswordChanged(false);

    if (newPassword !== repeatedPassword) {
      setPasswordError("The new passwords do not match.");
      return;
    }

    setBusy(true);

    try {
      await changePassword(currentPassword, newPassword);
      setCurrentPassword("");
      setNewPassword("");
      setRepeatedPassword("");
      setPasswordChanged(true);
    } catch (error) {
      setPasswordError(error instanceof Error ? error.message : "Could not change the password.");
    } finally {
      setBusy(false);
    }
  }

  async function runTwoFactor(action: () => Promise<void>) {
    setCodeError(null);
    setBusy(true);

    try {
      await action();
      await queryClient.invalidateQueries({ queryKey: currentUserQuery.queryKey });
    } catch (error) {
      setCodeError(error instanceof Error ? error.message : "Could not reach the server.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className={styles.page}>
      <h1>Account</h1>

      <section className={styles.card}>
        <h2 className={styles.title}>Password</h2>

        {fromDirectory ? (
          <p className={styles.hint}>
            This account comes from the directory. Change its password there.
          </p>
        ) : (
          <form
            className={styles.form}
            onSubmit={(event) => {
              event.preventDefault();
              void submitPassword();
            }}
          >
            <label className={styles.field}>
              <span>Current password</span>
              <input
                type="password"
                autoComplete="current-password"
                value={currentPassword}
                onChange={(event) => {
                  setCurrentPassword(event.target.value);
                }}
                required
              />
            </label>
            <label className={styles.field}>
              <span>New password</span>
              <input
                type="password"
                autoComplete="new-password"
                value={newPassword}
                onChange={(event) => {
                  setNewPassword(event.target.value);
                }}
                required
              />
            </label>
            <label className={styles.field}>
              <span>New password again</span>
              <input
                type="password"
                autoComplete="new-password"
                value={repeatedPassword}
                onChange={(event) => {
                  setRepeatedPassword(event.target.value);
                }}
                required
              />
            </label>

            <p className={styles.hint}>At least 12 characters.</p>

            {passwordError !== null && (
              <p className={styles.error} role="alert">
                {passwordError}
              </p>
            )}
            {passwordChanged && <p className={styles.done}>Password changed.</p>}

            <button type="submit" className={styles.submit} disabled={busy}>
              Change password
            </button>
          </form>
        )}
      </section>

      <section className={styles.card}>
        <h2 className={styles.title}>Authenticator</h2>

        {user?.twoFactorEnabled === true ? (
          <>
            <p className={styles.hint}>
              This account asks for a code from your authenticator app when you sign in.
            </p>
            <label className={styles.field}>
              <span>Authenticator code</span>
              <input
                inputMode="numeric"
                autoComplete="one-time-code"
                value={code}
                onChange={(event) => {
                  setCode(event.target.value);
                }}
              />
            </label>
            <div className={styles.actions}>
              <button
                type="button"
                className={styles.submit}
                disabled={busy || code.length === 0}
                onClick={() => {
                  void runTwoFactor(async () => {
                    await disableTwoFactor(code);
                    setCode("");
                    setRecoveryCodes(null);
                  });
                }}
              >
                Turn off
              </button>
              <button
                type="button"
                className={styles.secondary}
                disabled={busy}
                onClick={() => {
                  void runTwoFactor(async () => {
                    setRecoveryCodes((await regenerateRecoveryCodes()).codes);
                  });
                }}
              >
                New recovery codes
              </button>
            </div>
          </>
        ) : enrollment === null ? (
          <>
            <p className={styles.hint}>
              A second factor protects this account even if the password is captured, for example at
              a machine being deployed.
            </p>
            <button
              type="button"
              className={styles.submit}
              disabled={busy}
              onClick={() => {
                void runTwoFactor(async () => {
                  setEnrollment(await startTwoFactorEnrollment());
                });
              }}
            >
              Set up an authenticator
            </button>
          </>
        ) : (
          <>
            <p className={styles.hint}>
              Add this key to your authenticator app, then enter the code it shows.
            </p>
            <p className={styles.key}>{enrollment.sharedKey}</p>
            <p className={styles.hint}>
              Apps that read links can use{" "}
              <span className={styles.uri}>{enrollment.authenticatorUri}</span>
            </p>
            <label className={styles.field}>
              <span>Authenticator code</span>
              <input
                inputMode="numeric"
                autoComplete="one-time-code"
                value={code}
                onChange={(event) => {
                  setCode(event.target.value);
                }}
              />
            </label>
            <button
              type="button"
              className={styles.submit}
              disabled={busy || code.length === 0}
              onClick={() => {
                void runTwoFactor(async () => {
                  setRecoveryCodes((await enableTwoFactor(code)).codes);
                  setEnrollment(null);
                  setCode("");
                });
              }}
            >
              Turn on
            </button>
          </>
        )}

        {codeError !== null && (
          <p className={styles.error} role="alert">
            {codeError}
          </p>
        )}

        {recoveryCodes !== null && (
          <div className={styles.recovery}>
            <h3 className={styles.title}>Recovery codes</h3>
            <p className={styles.hint}>
              Each code signs you in once if you lose the authenticator. They are shown only now.
            </p>
            <ul className={styles.codes}>
              {recoveryCodes.map((recoveryCode) => (
                <li key={recoveryCode}>{recoveryCode}</li>
              ))}
            </ul>
          </div>
        )}
      </section>
    </div>
  );
}
