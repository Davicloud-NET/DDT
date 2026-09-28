// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { TextField } from "@/ui/TextField";

import { FieldLabel } from "./FieldLabel";
import type { InputFieldProps } from "./inputField";

export function TextInputField({ input, draft, hint, error, onChange }: InputFieldProps) {
  return (
    <TextField
      label={<FieldLabel input={input} />}
      value={draft.value}
      onChange={(value) => {
        onChange({ ...draft, value });
      }}
      isRequired={input.required}
      isInvalid={error !== null}
      errorMessage={error}
      {...(input.maxLength === null ? {} : { maxLength: input.maxLength })}
      {...(hint === null ? {} : { hint })}
      autoComplete="off"
    />
  );
}
