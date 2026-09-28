// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import type { CurrentUser } from "@/auth/auth";
import { Button } from "@/ui/Button";
import { Notice } from "@/ui/Notice";
import { TextField } from "@/ui/TextField";

import type { DirectoryProof, LdapForm } from "../signIn";

import { TestResult } from "./TestResult";
import { useDirectoryTest } from "./useDirectoryTest";

// Tries the values in the form before they are saved: the bind, and with a user name and password, that user's
// sign-in without a session. A test of one's own sign-in that keeps one an administrator hands back a proof.
export function DirectoryTest({
  form,
  me,
  proof,
  onProof,
}: {
  form: LdapForm;
  me: CurrentUser;
  proof: DirectoryProof | null;
  onProof: (proof: DirectoryProof) => void;
}) {
  const { userName, setUserName, password, setPassword, test } = useDirectoryTest(me, onProof);

  return (
    <div className="flex flex-col gap-3">
      <p className="max-w-[80ch] type-small text-ink-2">
        <Trans>
          Tries the values above as they are, before you save them. With a user name and password,
          DDT also signs that user in, without starting a session, and shows their groups and the
          role they would get. A wrong password counts towards the account's lockout.
        </Trans>
      </p>
      <form
        className="flex flex-wrap items-start gap-3"
        onSubmit={(event) => {
          event.preventDefault();

          if (form.values !== null) {
            test.mutate({ values: form.values, secrets: form.secrets });
          }
        }}
      >
        <TextField
          label={<Trans>User name, optional</Trans>}
          autoComplete="off"
          spellCheck="false"
          value={userName}
          onChange={setUserName}
          className="w-60"
        />
        <TextField
          label={<Trans>Password, optional</Trans>}
          type="password"
          autoComplete="off"
          value={password}
          onChange={setPassword}
          className="w-60"
        />
        <Button type="submit" isDisabled={test.isPending} className="mt-6.5">
          <Trans>Test the directory</Trans>
        </Button>
      </form>
      {test.isError ? <Notice tone="fail">{test.error.message}</Notice> : null}
      {test.isSuccess ? (
        <TestResult
          result={test.data}
          map={test.variables.values.groupRoleMap}
          proven={proof !== null && proof.token === test.data.proof}
        />
      ) : null}
    </div>
  );
}
