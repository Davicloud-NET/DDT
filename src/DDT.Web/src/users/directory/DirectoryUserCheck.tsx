// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import { useMutation } from "@tanstack/react-query";
import { useState } from "react";

import { Button } from "@/ui/Button";
import { Notice } from "@/ui/Notice";
import { TextField } from "@/ui/TextField";

import { checkDirectoryUser } from "../users";
import { fieldErrors } from "../userView";
import { DirectoryCheckResult } from "./DirectoryCheckResult";

// What a sign-in with a user name would give, found without the user's password.
export function DirectoryUserCheck() {
  const [userName, setUserName] = useState("");

  const check = useMutation({
    mutationFn: () => checkDirectoryUser(userName.trim()),
  });

  const errors = fieldErrors(check.error, "userName");

  return (
    <section className="flex flex-col gap-3">
      <h3 className="type-label text-ink">
        <Trans>Check a user</Trans>
      </h3>
      <form
        className="flex items-end gap-2"
        onSubmit={(event) => {
          event.preventDefault();
          check.mutate();
        }}
      >
        <TextField
          label={<Trans>User name</Trans>}
          autoComplete="off"
          spellCheck="false"
          value={userName}
          onChange={(value) => {
            setUserName(value);
          }}
          isInvalid={errors.length > 0}
          errorMessage={errors.join(" ")}
          className="min-w-0 flex-1"
        />
        <Button type="submit" isDisabled={check.isPending || userName.trim() === ""}>
          <Trans>Check</Trans>
        </Button>
      </form>
      {check.isError && errors.length === 0 ? (
        <Notice tone="fail">{check.error.message}</Notice>
      ) : null}
      {check.isSuccess ? <DirectoryCheckResult result={check.data} /> : null}
    </section>
  );
}
