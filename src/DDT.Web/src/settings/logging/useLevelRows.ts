// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useState } from "react";

import { equalJson } from "@/lib/equalJson";

import type { SettingsForm } from "../useSettingsForm";

import {
  DEFAULT_LOG_LEVELS,
  levelsOf,
  rowsOf,
  type CategoryLevel,
  type LoggingSettings,
} from "./logLevels";

// The rows as typed. They're kept while they still match what the form holds, so a row keeps its place and a blank
// one stays while its category is typed. A discard or a change saved elsewhere replaces them.
export function useLevelRows(
  form: SettingsForm<LoggingSettings>,
  logLevel: Record<string, string>,
) {
  const [typed, setTyped] = useState<CategoryLevel[] | null>(null);
  const rows = typed !== null && equalJson(levelsOf(typed), logLevel) ? typed : rowsOf(logLevel);

  return {
    rows,
    change: (next: CategoryLevel[]) => {
      setTyped(next);
      form.change("logLevel", levelsOf(next));
    },
    restoreDefaults: () => {
      setTyped(null);
      form.change("logLevel", DEFAULT_LOG_LEVELS);
    },
  };
}
