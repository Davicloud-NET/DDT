// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { ReactNode } from "react";

import { Button } from "@/ui/Button";
import { TextField } from "@/ui/TextField";

import type { RoleMapAdd } from "./useRoleMapAdd";

export function RoleMapAddForm({
  adding,
  label,
  hint,
  placeholder,
  action,
}: {
  adding: RoleMapAdd;
  label: ReactNode;
  hint?: ReactNode;
  placeholder: string;
  action: ReactNode;
}) {
  return (
    <form
      className="flex flex-wrap items-start gap-2"
      onSubmit={(event) => {
        event.preventDefault();
        adding.add();
      }}
    >
      <TextField
        label={label}
        {...(hint === undefined ? {} : { hint })}
        placeholder={placeholder}
        mono
        autoComplete="off"
        spellCheck="false"
        value={adding.typed}
        onChange={adding.type}
        isInvalid={adding.refused !== null}
        errorMessage={adding.refused}
        className="min-w-64 flex-1"
      />
      <Button type="submit" isDisabled={adding.typed.trim() === ""} className="mt-6.5">
        {action}
      </Button>
    </form>
  );
}
