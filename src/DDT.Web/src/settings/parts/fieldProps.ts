// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { ReactNode } from "react";

import type { SettingsForm } from "../useSettingsForm";

export interface SettingFieldProps<T> {
  form: SettingsForm<T>;
  field: string;
  label: ReactNode;
  hint?: ReactNode;
  canChange: boolean;
}

// A field is read-only for someone who may not change it, and while configuration sets it.
export function fieldProps<T>(form: SettingsForm<T>, field: string, canChange: boolean) {
  const errors = form.fieldErrors(field);

  return {
    isReadOnly: !canChange || form.lockOf(field) !== null,
    isInvalid: errors.length > 0,
    errorMessage: errors.join(" "),
  };
}
