// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { ReactNode } from "react";

import type { SettingsForm } from "../useSettingsForm";

import { LockNote } from "./LockNote";

// A settings field, with the note under it while configuration sets it.
export function FieldFrame<T>({
  form,
  field,
  children,
}: {
  form: SettingsForm<T>;
  field: string;
  children: ReactNode;
}) {
  const lock = form.lockOf(field);

  return (
    <div className="flex flex-col gap-1">
      {children}
      {lock === null ? null : <LockNote lock={lock} />}
    </div>
  );
}
