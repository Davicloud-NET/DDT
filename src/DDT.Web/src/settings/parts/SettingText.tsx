// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { TextField } from "@/ui/TextField";

import { valueAt } from "../valuePath";

import { FieldFrame } from "./FieldFrame";
import { fieldProps, type SettingFieldProps } from "./fieldProps";

export function SettingText<T>({
  form,
  field,
  label,
  hint,
  canChange,
  mono = false,
  placeholder,
}: SettingFieldProps<T> & { mono?: boolean; placeholder?: string }) {
  const value = valueAt(form.values, field);

  return (
    <FieldFrame form={form} field={field}>
      <TextField
        label={label}
        {...(hint === undefined ? {} : { hint })}
        {...(placeholder === undefined ? {} : { placeholder })}
        mono={mono}
        value={typeof value === "string" ? value : ""}
        onChange={(next) => {
          form.change(field, next === "" ? null : next);
        }}
        {...fieldProps(form, field, canChange)}
      />
    </FieldFrame>
  );
}
