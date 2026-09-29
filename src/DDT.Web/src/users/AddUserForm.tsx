// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { Notice } from "@/ui/Notice";
import { Select } from "@/ui/Select";
import { TextField } from "@/ui/TextField";

import { RoleOptions } from "./RoleOptions";
import type { AddUserState } from "./useAddUser";
import type { UserRole } from "./users";

// The add dialog's form. The dialog's submit button points at it by its id.
export function AddUserForm({ form }: { form: AddUserState }) {
  const { create, errors } = form;

  return (
    <form
      id="add-user"
      className="flex flex-col gap-4"
      onSubmit={(event) => {
        event.preventDefault();
        create.mutate();
      }}
    >
      <p>
        <Trans>
          DDT makes up a password for the account and shows it once. Its owner replaces it at the
          first sign-in. Directory and single sign-on accounts are not added here: they appear after
          their first sign-in.
        </Trans>
      </p>
      <TextField
        label={<Trans>User name</Trans>}
        hint={<Trans>What the account signs in with.</Trans>}
        autoFocus
        autoComplete="off"
        spellCheck="false"
        value={form.userName}
        onChange={form.setUserName}
        isRequired
        isInvalid={errors("userName").length > 0}
        errorMessage={errors("userName").join(" ")}
      />
      <TextField
        label={<Trans>Name</Trans>}
        hint={<Trans>The name DDT shows for the account, such as the person's full name.</Trans>}
        value={form.displayName}
        onChange={form.setDisplayName}
        isRequired
        isInvalid={errors("displayName").length > 0}
        errorMessage={errors("displayName").join(" ")}
      />
      <TextField
        label={<Trans>Email address</Trans>}
        hint={<Trans>Optional.</Trans>}
        type="email"
        value={form.email}
        onChange={form.setEmail}
        isInvalid={errors("email").length > 0}
        errorMessage={errors("email").join(" ")}
      />
      <Select
        label={<Trans>Role</Trans>}
        value={form.role}
        onChange={(key) => {
          if (key !== null) {
            form.setRole(String(key) as UserRole);
          }
        }}
        isInvalid={errors("role").length > 0}
        errorMessage={errors("role").join(" ")}
      >
        <RoleOptions />
      </Select>
      {form.general !== null ? <Notice tone="fail">{form.general}</Notice> : null}
    </form>
  );
}
