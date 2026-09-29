// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { Notice } from "@/ui/Notice";
import { NumberField } from "@/ui/NumberField";
import { Select } from "@/ui/Select";
import { TextField } from "@/ui/TextField";
import { RoleOptions } from "@/users/RoleOptions";

import type { TokenRole } from "./tokens";
import { TOKEN_DAYS } from "./tokenView";
import type { MakeTokenState } from "./useMakeToken";

// The make dialog's form. The dialog's submit button points at it by its id.
export function MakeTokenForm({ form }: { form: MakeTokenState }) {
  const { make, errors, days, expires } = form;

  return (
    <form
      id="make-token"
      className="flex flex-col gap-4"
      onSubmit={(event) => {
        event.preventDefault();
        if (Number.isFinite(days)) {
          make.mutate();
        }
      }}
    >
      <p>
        <Trans>
          A token lets a script call DDT's API as you. It stops working when it expires, when it is
          revoked, or when your account is disabled.
        </Trans>
      </p>
      <TextField
        label={<Trans>Name</Trans>}
        hint={<Trans>What uses it, such as the inventory script.</Trans>}
        autoFocus
        autoComplete="off"
        maxLength={64}
        value={form.name}
        onChange={form.setName}
        isRequired
        isInvalid={errors("name").length > 0}
        errorMessage={errors("name").join(" ")}
      />
      <Select
        label={<Trans>Role</Trans>}
        hint={<Trans>At most your own. Give it no more than the script needs.</Trans>}
        value={form.role}
        onChange={(key) => {
          if (key !== null) {
            form.setRole(String(key) as TokenRole);
          }
        }}
        isInvalid={errors("role").length > 0}
        errorMessage={errors("role").join(" ")}
      >
        <RoleOptions roles={form.roles} />
      </Select>
      <NumberField
        label={<Trans>Lifetime in days</Trans>}
        hint={
          expires === "" ? (
            <Trans>1 to 365 days.</Trans>
          ) : (
            <Trans>1 to 365 days. It stops working on {expires}.</Trans>
          )
        }
        minValue={TOKEN_DAYS.min}
        maxValue={TOKEN_DAYS.max}
        value={days}
        onChange={form.setDays}
        isInvalid={errors("expiresInDays").length > 0}
        errorMessage={errors("expiresInDays").join(" ")}
      />
      {form.general !== null ? <Notice tone="fail">{form.general}</Notice> : null}
    </form>
  );
}
