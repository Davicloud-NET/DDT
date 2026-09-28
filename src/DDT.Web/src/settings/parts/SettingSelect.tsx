// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { ReactNode } from "react";

import { ListBoxItem, Select } from "@/ui/Select";

import { valueAt } from "../valuePath";

import { FieldFrame } from "./FieldFrame";
import { fieldProps, type SettingFieldProps } from "./fieldProps";

export interface SettingOption {
  // The value as the section stores it, such as "Ldaps".
  id: string;
  label: string;
  description?: ReactNode;
}

// One value of a closed list, such as a transport or a role. With empty set, the field can also hold null, shown as
// that option's label.
export function SettingSelect<T>({
  form,
  field,
  label,
  hint,
  canChange,
  options,
  empty,
  className = "max-w-80",
}: SettingFieldProps<T> & { options: SettingOption[]; empty?: string; className?: string }) {
  const value = valueAt(form.values, field);
  const { isReadOnly, isInvalid, errorMessage } = fieldProps(form, field, canChange);
  const all = empty === undefined ? options : [{ id: "", label: empty }, ...options];

  return (
    <FieldFrame form={form} field={field}>
      <Select
        label={label}
        {...(hint === undefined ? {} : { hint })}
        value={typeof value === "string" ? value : ""}
        onChange={(key) => {
          if (key !== null) {
            form.change(field, key === "" ? null : String(key));
          }
        }}
        isDisabled={isReadOnly}
        isInvalid={isInvalid}
        errorMessage={errorMessage}
        className={className}
      >
        {all.map((option) => (
          <ListBoxItem
            key={option.id}
            id={option.id}
            textValue={option.label}
            {...(option.description === undefined ? {} : { description: option.description })}
          >
            {option.label}
          </ListBoxItem>
        ))}
      </Select>
    </FieldFrame>
  );
}
