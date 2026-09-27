// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import { IconPlus, IconX } from "@tabler/icons-react";
import { useState, type ReactNode } from "react";
import { Button as AriaButton } from "react-aria-components";

import { equalJson } from "@/lib/equalJson";
import { Button } from "@/ui/Button";
import { cx } from "@/ui/cx";
import { Skeleton } from "@/ui/Layout";
import { Notice } from "@/ui/Notice";
import { ListBoxItem, Select } from "@/ui/Select";
import { TextField } from "@/ui/TextField";

import { LockNote, SettingsSection } from "./SettingsParts";
import { useSettingsForm, type SettingsForm } from "./useSettingsForm";

export interface LoggingSettings {
  // The level per category; Default for every category not listed.
  logLevel: Record<string, string>;
}

// The levels DDT starts with, as the server's code sets them.
const defaultLogLevels: Record<string, string> = {
  Default: "Information",
  "Microsoft.AspNetCore": "Warning",
  "Microsoft.EntityFrameworkCore.Database.Command": "Warning",
  "Microsoft.EntityFrameworkCore.Infrastructure": "Warning",
};

const levels = ["Trace", "Debug", "Information", "Warning", "Error", "Critical", "None"] as const;

interface Row {
  id: number;
  category: string;
  level: string;
  // The Default row stays: it is what every other category logs at.
  fixed: boolean;
}

function rowsOf(logLevel: Record<string, string>): Row[] {
  return Object.entries(logLevel).map(([category, level], id) => ({
    id,
    category,
    level,
    fixed: category.toLowerCase() === "default",
  }));
}

// Rows without a category are still being typed, and are not part of the section yet.
function levelsOf(rows: Row[]): Record<string, string> {
  return Object.fromEntries(
    rows.filter((row) => row.category.trim() !== "").map((row) => [row.category.trim(), row.level]),
  );
}

// How much the server writes to its log, per category. A change applies at once, without a restart.
export function LoggingPanel() {
  const form = useSettingsForm<LoggingSettings>("logging");

  if (form.view === null || form.values === null) {
    return form.query.isError ? (
      <Notice tone="fail">
        <Trans>These settings could not be loaded.</Trans>
      </Notice>
    ) : (
      <Skeleton className="h-64 w-full" />
    );
  }

  return (
    <SettingsSection
      form={form}
      canChange
      title={<Trans>Log levels</Trans>}
      description={
        <Trans>
          How much the server writes to its log, per category. A category covers the categories
          below it, so DDT.Pxe covers DDT.Pxe.Tftp as well, and Default applies to every category
          not listed. A change applies at once.
        </Trans>
      }
    >
      <LevelRows form={form} logLevel={form.values.logLevel} />
      <p className="max-w-[80ch] type-small text-ink-2">
        <Trans>
          When a machine does not netboot, set DDT.Pxe to Debug or Trace and let the machine try
          again: the log then says what it asked for and what the server answered. Set it back
          afterwards, since Trace writes a line for every packet.
        </Trans>
      </p>
    </SettingsSection>
  );
}

function LevelRows({
  form,
  logLevel,
}: {
  form: SettingsForm<LoggingSettings>;
  logLevel: Record<string, string>;
}) {
  const { t } = useLingui();
  const lock = form.lockOf("logLevel");
  const locked = lock !== null;
  // The rows as typed, kept while they still say what the form holds, so a row keeps its place and a blank one stays
  // while its category is typed; a discard or a change saved elsewhere replaces them.
  const [typed, setTyped] = useState<Row[] | null>(null);
  const rows = typed !== null && equalJson(levelsOf(typed), logLevel) ? typed : rowsOf(logLevel);
  const isDefault = equalJson(logLevel, defaultLogLevels);
  const sectionErrors = form.fieldErrors("logLevel");

  const change = (next: Row[]) => {
    setTyped(next);
    form.change("logLevel", levelsOf(next));
  };

  return (
    <div className="flex flex-col gap-3">
      <ul aria-label={t`Log levels`} className="flex flex-col gap-3">
        {rows.map((row, index) => {
          const category = row.category.trim();
          const twice = rows
            .slice(index + 1)
            .some((other) => other.category.trim().toLowerCase() === category.toLowerCase());
          const errors =
            category === ""
              ? []
              : [
                  ...(twice ? [t`${category} is listed again below, whose level applies.`] : []),
                  ...form.fieldErrors(`logLevel[${category}]`),
                ];
          const known = levels.find((level) => level.toLowerCase() === row.level.toLowerCase());
          // The first row's labels head the columns; the rows below keep theirs for screen readers only.
          const shown = (label: ReactNode) =>
            index === 0 ? label : <span className="sr-only">{label}</span>;

          return (
            <li
              key={row.id}
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
                  change(
                    rows.map((other) =>
                      other.id === row.id ? { ...other, category: value } : other,
                    ),
                  );
                }}
              />
              <Select
                label={shown(
                  category === "" ? (
                    <Trans>Level</Trans>
                  ) : (
                    <Trans>
                      Level <span className="sr-only">of {category}</span>
                    </Trans>
                  ),
                )}
                value={known ?? row.level}
                isDisabled={locked}
                onChange={(key) => {
                  if (key !== null) {
                    const level = String(key);

                    change(
                      rows.map((other) => (other.id === row.id ? { ...other, level } : other)),
                    );
                  }
                }}
              >
                {levels.map((level) => (
                  <ListBoxItem key={level} id={level} description={<LevelMeaning level={level} />}>
                    {level}
                  </ListBoxItem>
                ))}
              </Select>
              {locked || row.fixed ? (
                <span />
              ) : (
                <AriaButton
                  aria-label={category === "" ? t`Remove this row` : t`Remove ${category}`}
                  onPress={() => {
                    change(rows.filter((other) => other.id !== row.id));
                  }}
                  className={cx(
                    index === 0 ? "mt-7" : "mt-1.5",
                    "flex size-9 cursor-pointer items-center justify-center rounded-key text-muted outline-none hover:bg-hover hover:text-ink focus-visible:outline-2 focus-visible:outline-focus",
                  )}
                >
                  <IconX size={16} stroke={2} />
                </AriaButton>
              )}
            </li>
          );
        })}
      </ul>
      {sectionErrors.length > 0 ? (
        <span className="type-small text-fail-text">{sectionErrors.join(" ")}</span>
      ) : null}
      {lock !== null ? <LockNote lock={lock} /> : null}
      {locked ? null : (
        <span className="flex flex-wrap items-center gap-3">
          <Button
            size="sm"
            onPress={() => {
              change([
                ...rows,
                {
                  id: Math.max(-1, ...rows.map((row) => row.id)) + 1,
                  category: "",
                  level: "Debug",
                  fixed: false,
                },
              ]);
            }}
          >
            <IconPlus size={14} stroke={2} aria-hidden="true" />
            <Trans>Add a category</Trans>
          </Button>
          <Button
            size="sm"
            variant="quiet"
            isDisabled={isDefault}
            onPress={() => {
              setTyped(null);
              form.change("logLevel", defaultLogLevels);
            }}
          >
            <Trans>Use the defaults</Trans>
          </Button>
          <span className="type-small text-muted">
            <Trans>
              The defaults: Default at Information, and Microsoft.AspNetCore and the two Entity
              Framework Core categories at Warning.
            </Trans>
          </span>
        </span>
      )}
    </div>
  );
}

function LevelMeaning({ level }: { level: (typeof levels)[number] }) {
  switch (level) {
    case "Trace":
      return <Trans>Everything, a line for every packet and request</Trans>;
    case "Debug":
      return <Trans>Details for finding a fault</Trans>;
    case "Information":
      return <Trans>The normal course of events</Trans>;
    case "Warning":
      return <Trans>Only what went unexpectedly</Trans>;
    case "Error":
      return <Trans>Only failures</Trans>;
    case "Critical":
      return <Trans>Only failures that stop the server or a part of it</Trans>;
    case "None":
      return <Trans>Nothing</Trans>;
  }
}
