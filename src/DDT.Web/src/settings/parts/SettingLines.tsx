// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useState } from "react";

import { equalJson } from "@/lib/equalJson";
import { TextField } from "@/ui/TextField";

import { valueAt } from "../valuePath";

import { FieldFrame } from "./FieldFrame";
import { fieldProps, type SettingFieldProps } from "./fieldProps";

// A list typed one entry per line, such as networks or addresses.
export function SettingLines<T>({ form, field, label, hint, canChange }: SettingFieldProps<T>) {
  const value = valueAt(form.values, field);
  const lines = Array.isArray(value)
    ? value.filter((line): line is string => typeof line === "string")
    : [];
  // The typed text, blank lines and spaces included, kept while it still matches the form. A discard or a save
  // from elsewhere replaces it.
  const [text, setText] = useState<string | null>(null);
  const shown = text !== null && equalJson(linesOf(text), lines) ? text : lines.join("\n");

  return (
    <FieldFrame form={form} field={field}>
      <TextField
        label={label}
        {...(hint === undefined ? {} : { hint })}
        multiline
        rows={4}
        mono
        value={shown}
        onChange={(next) => {
          setText(next);
          form.change(field, linesOf(next));
        }}
        onBlur={() => {
          setText(null);
        }}
        {...fieldProps(form, field, canChange)}
      />
    </FieldFrame>
  );
}

function linesOf(text: string): string[] {
  return text
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter((line) => line !== "");
}
