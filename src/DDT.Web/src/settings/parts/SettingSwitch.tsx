// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { FieldErrorText } from "@/ui/FieldErrorText";
import { Switch } from "@/ui/Switch";

import { valueAt } from "../valuePath";

import { FieldFrame } from "./FieldFrame";
import type { SettingFieldProps } from "./fieldProps";

export function SettingSwitch<T>({ form, field, label, hint, canChange }: SettingFieldProps<T>) {
  const value = valueAt(form.values, field);

  return (
    <FieldFrame form={form} field={field}>
      <Switch
        isSelected={value === true}
        onChange={(next) => {
          form.change(field, next);
        }}
        isReadOnly={!canChange || form.lockOf(field) !== null}
      >
        {label}
      </Switch>
      {hint ? <span className="type-small text-muted">{hint}</span> : null}
      <FieldErrorText errors={form.fieldErrors(field)} />
    </FieldFrame>
  );
}
