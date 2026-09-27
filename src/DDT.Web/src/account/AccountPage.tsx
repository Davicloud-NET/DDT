// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { Trans, useLingui } from "@lingui/react/macro";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";

import {
  changePassword,
  disableTwoFactor,
  enableTwoFactor,
  regenerateRecoveryCodes,
  startTwoFactorEnrollment,
  type TwoFactorEnrollment,
} from "@/auth/account";
import { currentUserQuery, type CurrentUser } from "@/auth/auth";
import { OwnTokensPanel } from "@/tokens/OwnTokensPanel";
import { Button } from "@/ui/Button";
import { Dialog } from "@/ui/Dialog";
import { Facts, Page, PageHeader, Panel } from "@/ui/Layout";
import { Notice } from "@/ui/Notice";
import { QrCode } from "@/ui/QrCode";
import { StateTag } from "@/ui/StateTag";
import { TextField } from "@/ui/TextField";
import { roleLabel, sourceLabel } from "@/users/userView";

// The signed-in person's own account: who they are to DDT, their password, their authenticator and their API tokens.
// An account signed in with a password an administrator was shown sees nothing else until it has set its own: the
// server answers nothing else, and the shell keeps it on this page.
export function AccountPage() {
  const user = useQuery(currentUserQuery).data ?? null;

  if (user === null) {
    return null;
  }

  return (
    <Page className="max-w-[72rem]">
      <PageHeader title={<Trans>Account and security</Trans>} />
      {user.mustChangePassword ? (
        <Notice tone="attention" title={<Trans>Set a password of your own first</Trans>}>
          <Trans>
            You signed in with a password an administrator was shown. Replace it under Password;
            until then, DDT shows you nothing but this page.
          </Trans>
        </Notice>
      ) : null}
      <div className="grid items-start gap-4 lg:grid-cols-2">
        <div className="flex flex-col gap-4">
          <Profile user={user} />
          <PasswordPanel user={user} />
        </div>
        <AuthenticatorPanel user={user} />
      </div>
      {user.mustChangePassword ? null : <OwnTokensPanel user={user} />}
    </Page>
  );
}

function Profile({ user }: { user: CurrentUser }) {
  const roles = user.roles.map(roleLabel).join(", ");

  return (
    <Panel title={<Trans>You</Trans>}>
      <Facts
        items={[
          { label: <Trans>User name</Trans>, value: user.userName, mono: true },
          ...(user.displayName === null
            ? []
            : [{ label: <Trans>Name</Trans>, value: user.displayName }]),
          { label: <Trans>Account</Trans>, value: sourceLabel(user.source) },
          { label: <Trans>Role</Trans>, value: roles === "" ? t`None` : roles },
        ]}
      />
    </Panel>
  );
}

// Changing the password also ends the wait for one of the account's own: the server drops the demand with the change,
// and the cached account follows, which opens the rest of DDT again.
function PasswordPanel({ user }: { user: CurrentUser }) {
  const queryClient = useQueryClient();
  const [current, setCurrent] = useState("");
  const [next, setNext] = useState("");
  const [again, setAgain] = useState("");
  const [mismatch, setMismatch] = useState(false);
  const [changed, setChanged] = useState<"no" | "yes" | "unlocked">("no");

  const change = useMutation({
    mutationFn: () => changePassword(current, next),
    onSuccess: () => {
      setCurrent("");
      setNext("");
      setAgain("");
      setChanged(user.mustChangePassword ? "unlocked" : "yes");
      queryClient.setQueryData(currentUserQuery.queryKey, (cached) =>
        cached === undefined || cached === null ? cached : { ...cached, mustChangePassword: false },
      );
    },
  });

  if (user.source !== "Local") {
    return (
      <Panel title={<Trans>Password</Trans>}>
        <p className="text-ink-2">
          {user.source === "Directory" ? (
            <Trans>This account comes from the directory. Change its password there.</Trans>
          ) : (
            <Trans>This account signs in through single sign-on and has no password in DDT.</Trans>
          )}
        </p>
      </Panel>
    );
  }

  return (
    <Panel title={<Trans>Password</Trans>}>
      <form
        className="flex flex-col gap-3.5"
        onSubmit={(event) => {
          event.preventDefault();
          setMismatch(next !== again);
          setChanged("no");

          if (next === again) {
            change.mutate();
          }
        }}
      >
        <TextField
          label={<Trans>Current password</Trans>}
          {...(user.mustChangePassword
            ? { hint: <Trans>The password you were given and signed in with.</Trans> }
            : {})}
          type="password"
          autoComplete="current-password"
          value={current}
          onChange={setCurrent}
          isRequired
        />
        <TextField
          label={<Trans>New password</Trans>}
          hint={<Trans>At least 12 characters.</Trans>}
          type="password"
          autoComplete="new-password"
          value={next}
          onChange={setNext}
          minLength={12}
          isRequired
        />
        <TextField
          label={<Trans>New password again</Trans>}
          type="password"
          autoComplete="new-password"
          value={again}
          onChange={(value) => {
            setAgain(value);
            setMismatch(false);
          }}
          isRequired
          isInvalid={mismatch}
          errorMessage={<Trans>The new passwords do not match.</Trans>}
        />
        {change.isError ? <Notice tone="fail">{change.error.message}</Notice> : null}
        {changed === "yes" ? (
          <Notice tone="info">
            <Trans>Password changed.</Trans>
          </Notice>
        ) : null}
        {changed === "unlocked" ? (
          <Notice tone="info">
            <Trans>Password changed. The rest of DDT is open to you now.</Trans>
          </Notice>
        ) : null}
        <div>
          <Button type="submit" variant="primary" isDisabled={change.isPending}>
            <Trans>Change password</Trans>
          </Button>
        </div>
      </form>
    </Panel>
  );
}

// Turns the second factor on with a QR code or the key, off with a code, and makes new recovery codes. Each change
// replaces the signed-in user in the cache with what the server now says.
function AuthenticatorPanel({ user }: { user: CurrentUser }) {
  const { t: translate } = useLingui();
  const queryClient = useQueryClient();
  const [enrollment, setEnrollment] = useState<TwoFactorEnrollment | null>(null);
  const [code, setCode] = useState("");
  const [recoveryCodes, setRecoveryCodes] = useState<string[] | null>(null);
  const [copied, setCopied] = useState(false);

  const setEnabled = (twoFactorEnabled: boolean) => {
    queryClient.setQueryData(currentUserQuery.queryKey, (current) =>
      current === undefined || current === null ? current : { ...current, twoFactorEnabled },
    );
  };

  const start = useMutation({
    mutationFn: startTwoFactorEnrollment,
    onSuccess: (started) => {
      setEnrollment(started);
      setCode("");
    },
  });
  const enable = useMutation({
    mutationFn: () => enableTwoFactor(code.trim()),
    onSuccess: (result) => {
      setEnabled(true);
      setEnrollment(null);
      setCode("");
      setRecoveryCodes(result.codes);
      setCopied(false);
    },
  });
  const disable = useMutation({
    mutationFn: () => disableTwoFactor(code.trim()),
    onSuccess: () => {
      setEnabled(false);
      setCode("");
    },
  });
  const regenerate = useMutation({
    mutationFn: regenerateRecoveryCodes,
    onSuccess: (result) => {
      setRecoveryCodes(result.codes);
      setCopied(false);
    },
  });

  const failed = [start, enable, disable, regenerate].find((mutation) => mutation.isError);
  const busy = [start, enable, disable, regenerate].some((mutation) => mutation.isPending);
  const codeField = (
    <TextField
      label={<Trans>Code from the authenticator app</Trans>}
      inputMode="numeric"
      autoComplete="one-time-code"
      mono
      value={code}
      onChange={setCode}
      className="max-w-60"
    />
  );

  return (
    <Panel
      title={<Trans>Authenticator</Trans>}
      actions={
        user.twoFactorEnabled ? (
          <StateTag tone="ok">{translate`On`}</StateTag>
        ) : (
          <StateTag tone="idle">{translate`Off`}</StateTag>
        )
      }
    >
      {user.twoFactorEnabled ? (
        <>
          <p className="text-ink-2">
            <Trans>
              Signing in asks for a code from your authenticator app. To turn it off, enter a
              current code.
            </Trans>
          </p>
          {codeField}
          <div className="flex flex-wrap gap-2">
            <Button
              variant="danger"
              isDisabled={busy || code.trim() === ""}
              onPress={() => {
                disable.mutate();
              }}
            >
              <Trans>Turn off</Trans>
            </Button>
            <Button
              isDisabled={busy}
              onPress={() => {
                regenerate.mutate();
              }}
            >
              <Trans>Make new recovery codes</Trans>
            </Button>
          </div>
        </>
      ) : enrollment === null ? (
        <>
          <p className="text-ink-2">
            <Trans>
              A second factor protects this account even if its password is seen, for example while
              someone signs in at a machine being deployed.
            </Trans>
          </p>
          <div>
            <Button
              variant="primary"
              isDisabled={busy}
              onPress={() => {
                start.mutate();
              }}
            >
              <Trans>Set up an authenticator</Trans>
            </Button>
          </div>
        </>
      ) : (
        <form
          className="flex flex-col gap-3.5"
          onSubmit={(event) => {
            event.preventDefault();
            enable.mutate();
          }}
        >
          <p className="text-ink-2">
            <Trans>
              Scan the code with your authenticator app, or type the key into it. Then enter the
              code the app shows.
            </Trans>
          </p>
          <div className="flex flex-wrap items-center gap-5">
            <QrCode
              value={enrollment.authenticatorUri}
              label={translate`QR code for your authenticator app`}
              className="size-44 shrink-0"
            />
            <div className="flex min-w-0 flex-col gap-1">
              <span className="type-small text-muted">
                <Trans>Key</Trans>
              </span>
              <span className="type-data break-all text-ink">{groupKey(enrollment.sharedKey)}</span>
            </div>
          </div>
          {codeField}
          <div className="flex flex-wrap gap-2">
            <Button type="submit" variant="primary" isDisabled={busy || code.trim() === ""}>
              <Trans>Turn on</Trans>
            </Button>
            <Button
              isDisabled={busy}
              onPress={() => {
                setEnrollment(null);
                setCode("");
              }}
            >
              <Trans>Cancel</Trans>
            </Button>
          </div>
        </form>
      )}

      {failed?.error ? <Notice tone="fail">{failed.error.message}</Notice> : null}

      <Dialog
        isOpen={recoveryCodes !== null}
        onOpenChange={(open) => {
          if (!open) {
            setRecoveryCodes(null);
          }
        }}
        title={<Trans>Your recovery codes</Trans>}
        footer={
          <>
            <Button
              onPress={() => {
                void navigator.clipboard.writeText((recoveryCodes ?? []).join("\n")).then(() => {
                  setCopied(true);
                });
              }}
            >
              {copied ? <Trans>Copied</Trans> : <Trans>Copy</Trans>}
            </Button>
            <Button
              variant="primary"
              onPress={() => {
                setRecoveryCodes(null);
              }}
            >
              <Trans>I have saved them</Trans>
            </Button>
          </>
        }
      >
        <p>
          <Trans>
            Each code signs you in once if you lose the authenticator. Keep them somewhere safe:
            they are shown only now, and new ones replace these.
          </Trans>
        </p>
        <ul className="grid grid-cols-2 gap-x-6 gap-y-1.5 rounded-key bg-well p-4 type-data text-ink">
          {(recoveryCodes ?? []).map((recoveryCode) => (
            <li key={recoveryCode}>{recoveryCode}</li>
          ))}
        </ul>
      </Dialog>
    </Panel>
  );
}

// "ABCD EFGH IJKL", easier to type into an app than one long string.
function groupKey(key: string): string {
  return key
    .replace(/\s+/g, "")
    .replace(/(.{4})/g, "$1 ")
    .trim();
}
