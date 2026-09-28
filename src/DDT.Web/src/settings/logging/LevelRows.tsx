// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useLingui } from "@lingui/react/macro";

import { equalJson } from "@/lib/equalJson";
import { FieldErrorText } from "@/ui/FieldErrorText";

import { LockNote } from "../parts/LockNote";
import type { SettingsForm } from "../useSettingsForm";

import { LevelActions } from "./LevelActions";
import { LevelRow } from "./LevelRow";
import { DEFAULT_LOG_LEVELS, withNewRow, type LoggingSettings } from "./logLevels";
import { useLevelRows } from "./useLevelRows";

export function LevelRows({
  form,
  logLevel,
}: {
  form: SettingsForm<LoggingSettings>;
  logLevel: Record<string, string>;
}) {
  const { t } = useLingui();
  const lock = form.lockOf("logLevel");
  const locked = lock !== null;
  const { rows, change, restoreDefaults } = useLevelRows(form, logLevel);

  return (
    <div className="flex flex-col gap-3">
      <ul aria-label={t`Log levels`} className="flex flex-col gap-3">
        {rows.map((row, index) => (
          <LevelRow
            key={row.id}
            row={row}
            index={index}
            rows={rows}
            form={form}
            locked={locked}
            onChange={change}
          />
        ))}
      </ul>
      <FieldErrorText errors={form.fieldErrors("logLevel")} />
      {lock !== null ? <LockNote lock={lock} /> : null}
      {locked ? null : (
        <LevelActions
          isDefault={equalJson(logLevel, DEFAULT_LOG_LEVELS)}
          onAdd={() => {
            change(withNewRow(rows));
          }}
          onRestoreDefaults={restoreDefaults}
        />
      )}
    </div>
  );
}
