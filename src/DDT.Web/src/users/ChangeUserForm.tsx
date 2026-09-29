// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";

import { Notice } from "@/ui/Notice";
import { Select } from "@/ui/Select";
import { TextField } from "@/ui/TextField";

import { RoleHint } from "./RoleHint";
import { RoleOptions } from "./RoleOptions";
import type { ChangeUserState } from "./useChangeUser";
import type { UserRole, UserView } from "./users";

export function ChangeUserForm({ user, form }: { user: UserView; form: ChangeUserState }) {
  const { t } = useLingui();
  const { save, errors, roleNote } = form;

  return (
    <form
      id={form.formId}
      className="flex flex-col gap-4"
      onSubmit={(event) => {
        event.preventDefault();

        if (form.changed) {
          save.mutate();
        }
      }}
    >
      <TextField
        label={<Trans>Name</Trans>}
        value={form.displayName}
        onChange={form.setDisplayName}
        isDisabled={form.directory}
        hint={
          form.directory ? (
            <Trans>
              The directory provides the name and email address. DDT takes them over at each
              sign-in.
            </Trans>
          ) : (
            <Trans>Left empty, DDT shows the user name.</Trans>
          )
        }
        isInvalid={errors("displayName").length > 0}
        errorMessage={errors("displayName").join(" ")}
      />
      <TextField
        label={<Trans>Email address</Trans>}
        type="email"
        value={form.email}
        onChange={form.setEmail}
        isDisabled={form.directory}
        isInvalid={errors("email").length > 0}
        errorMessage={errors("email").join(" ")}
      />
      <Select
        label={<Trans>Role</Trans>}
        value={form.role}
        placeholder={t`No role`}
        isDisabled={form.roleLocked}
        hint={roleNote === null ? undefined : <RoleHint note={roleNote} user={user} />}
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
