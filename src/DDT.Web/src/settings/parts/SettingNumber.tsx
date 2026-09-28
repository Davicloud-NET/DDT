// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { NumberField } from "@/ui/NumberField";

import { valueAt } from "../valuePath";

import { FieldFrame } from "./FieldFrame";
import { fieldProps, type SettingFieldProps } from "./fieldProps";

export function SettingNumber<T>({
  form,
  field,
  label,
  hint,
  canChange,
  minValue,
  maxValue,
}: SettingFieldProps<T> & { minValue?: number; maxValue?: number }) {
  const value = valueAt(form.values, field);

  return (
    <FieldFrame form={form} field={field}>
      <NumberField
        label={label}
        {...(hint === undefined ? {} : { hint })}
        {...(minValue === undefined ? {} : { minValue })}
        {...(maxValue === undefined ? {} : { maxValue })}
        value={typeof value === "number" ? value : Number.NaN}
        onChange={(next) => {
          form.change(field, Number.isNaN(next) ? null : next);
        }}
        {...fieldProps(form, field, canChange)}
        className="max-w-60"
      />
    </FieldFrame>
  );
}
