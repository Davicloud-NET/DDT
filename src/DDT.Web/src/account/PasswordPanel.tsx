// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import type { CurrentUser } from "@/auth/auth";
import { Button } from "@/ui/Button";
import { Notice } from "@/ui/Notice";
import { Panel } from "@/ui/Panel";
import { TextField } from "@/ui/TextField";

import { usePasswordChange } from "./usePasswordChange";

export function PasswordPanel({ user }: { user: CurrentUser }) {
  const form = usePasswordChange(user);
  const { change, changed } = form;

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
          form.submit();
        }}
      >
        <TextField
          label={<Trans>Current password</Trans>}
          {...(user.mustChangePassword
            ? { hint: <Trans>The password you were given and signed in with.</Trans> }
            : {})}
          type="password"
          autoComplete="current-password"
          value={form.current}
          onChange={form.setCurrent}
          isRequired
        />
        <TextField
          label={<Trans>New password</Trans>}
          hint={<Trans>At least 12 characters.</Trans>}
          type="password"
          autoComplete="new-password"
          value={form.next}
          onChange={form.setNext}
          minLength={12}
          isRequired
        />
        <TextField
          label={<Trans>New password again</Trans>}
          type="password"
          autoComplete="new-password"
          value={form.again}
          onChange={form.changeAgain}
          isRequired
          isInvalid={form.mismatch}
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
