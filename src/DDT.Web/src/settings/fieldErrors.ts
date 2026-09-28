// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { settingsText, type SaveRefusal, type SettingsFinding } from "./settings";

// The last save's refusal and the stored section's problems for one field, in the person's language: apiErrorFrom
// says the refusal's already.
export function fieldErrors(
  refusal: SaveRefusal | null,
  problems: readonly SettingsFinding[],
  field: string,
): string[] {
  return [
    ...(refusal?.kind === "invalid" ? (refusal.fields[field] ?? []) : []),
    ...problems.filter((p) => p.field === field).map((p) => settingsText(p)),
  ];
}
