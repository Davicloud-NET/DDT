// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import { IconX } from "@tabler/icons-react";
import type { ReactNode } from "react";
import { Button as AriaButton } from "react-aria-components";

import { cx } from "@/ui/cx";
import { TextField } from "@/ui/TextField";

import type { SettingsForm } from "../useSettingsForm";

import { LevelSelect } from "./LevelSelect";
import {
  isListedAgain,
  withCategory,
  withLevel,
  withoutRow,
  type CategoryLevel,
  type LoggingSettings,
} from "./logLevels";

interface LevelRowProps {
  row: CategoryLevel;
  index: number;
  rows: CategoryLevel[];
  form: SettingsForm<LoggingSettings>;
  locked: boolean;
  onChange: (rows: CategoryLevel[]) => void;
}

export function LevelRow({ row, index, rows, form, locked, onChange }: LevelRowProps) {
  const { t } = useLingui();
  const category = row.category.trim();
  const errors =
    category === ""
      ? []
      : [
          ...(isListedAgain(rows, index)
            ? [t`${category} is listed again below, whose level applies.`]
            : []),
          ...form.fieldErrors(`logLevel[${category}]`),
        ];
  // The first row's labels head the columns; the rows below keep theirs for screen readers only.
  const shown = (label: ReactNode) =>
    index === 0 ? label : <span className="sr-only">{label}</span>;

  return (
    <li
      // On a phone the category takes a line of its own, above its level.
      className="grid grid-cols-[minmax(0,1fr)_2.25rem] items-start gap-3 border-t border-line-soft pt-3 first:border-t-0 first:pt-0 sm:grid-cols-[minmax(0,1fr)_minmax(9rem,13rem)_2.25rem] sm:border-t-0 sm:pt-0"
    >
      <TextField
        className="col-span-2 sm:col-span-1"
        label={shown(<Trans>Category</Trans>)}
        mono
        value={row.category}
        placeholder="DDT.Pxe"
        autoComplete="off"
        spellCheck="false"
        isReadOnly={locked || row.fixed}
        isInvalid={errors.length > 0}
        errorMessage={errors.join(" ")}
        onChange={(value) => {
          onChange(withCategory(rows, row.id, value));
        }}
      />
      <LevelSelect
        label={shown(
          category === "" ? (
            <Trans>Level</Trans>
          ) : (
            <Trans>
              Level <span className="sr-only">of {category}</span>
            </Trans>
          ),
        )}
        value={row.level}
        isDisabled={locked}
        onChange={(level) => {
          onChange(withLevel(rows, row.id, level));
        }}
      />
      {locked || row.fixed ? (
        <span />
      ) : (
        <AriaButton
          aria-label={category === "" ? t`Remove this row` : t`Remove ${category}`}
          onPress={() => {
            onChange(withoutRow(rows, row.id));
          }}
          className={cx(
            index === 0 ? "mt-7" : "mt-1.5",
            "flex size-9 cursor-pointer items-center justify-center rounded-key text-muted key-motion outline-none hover:bg-hover pressed:bg-key-quiet-pressed hover:text-ink focus-visible:outline-2 focus-visible:outline-focus",
          )}
        >
          <IconX size={16} stroke={2} />
        </AriaButton>
      )}
    </li>
  );
}
