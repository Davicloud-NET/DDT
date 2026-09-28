// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import { IconPlus, IconX } from "@tabler/icons-react";
import { useContext, useId, type ReactNode } from "react";
import { Button as AriaButton } from "react-aria-components";

import { TemplateField } from "@/sequences/builder/TemplateField";
import { EditorLock } from "@/sequences/editorLock";
import { TextSetting } from "@/sequences/fields/TextSetting";
import { fieldFindings, type Findings } from "@/sequences/problems";
import { cx } from "@/ui/cx";

import type { EditedValue } from "./values";

// The values a rule or a machine role sets: a name in the mono face, such as TimeZone, and its value, a template that
// may use the machine's facts and other values. Each row's fields are named values[1].name and values[1].value, as
// the server's findings name them. It needs the builder's data around it for the completion of names.

const iconKey =
  "flex size-8 cursor-pointer items-center justify-center rounded-key text-muted key-motion outline-none " +
  "hover:bg-hover hover:text-ink pressed:bg-key-quiet-pressed focus-visible:outline-2 focus-visible:outline-focus";

export function ValuesEditor({
  label,
  hint,
  rows,
  findings,
  onChange,
}: {
  label: ReactNode;
  hint?: ReactNode;
  rows: readonly EditedValue[];
  findings: Findings;
  onChange: (rows: EditedValue[]) => void;
}) {
  const { t } = useLingui();
  const locked = useContext(EditorLock);
  const labelId = useId();
  const own = fieldFindings(findings, "values");

  const change = (key: string, patch: Partial<EditedValue>) => {
    onChange(rows.map((row) => (row.key === key ? { ...row, ...patch } : row)));
  };

  return (
    <div role="group" aria-labelledby={labelId} data-field="values" className="flex flex-col gap-2">
      <span id={labelId} className="type-label text-ink">
        {label}
      </span>
      {hint ? <span className="-mt-1 type-small text-muted">{hint}</span> : null}
      {rows.length === 0 ? (
        <p className="type-small text-muted">
          <Trans>No values.</Trans>
        </p>
      ) : null}
      {rows.map((row, index) => {
        const number = index + 1;
        const name = row.name.trim() === "" ? t`value ${number}` : row.name.trim();

        return (
          <div
            key={row.key}
            className={cx(
              "grid items-start gap-1.5",
              locked
                ? "grid-cols-[minmax(0,11rem)_minmax(0,1fr)]"
                : "grid-cols-[minmax(0,11rem)_minmax(0,1fr)_2rem]",
            )}
          >
            <TextSetting
              label={<span className="sr-only">{t`Name of value ${number}`}</span>}
              field={`values[${String(index)}].name`}
              findings={findings}
              mono
              value={row.name}
              placeholder="TimeZone"
              onChange={(text) => {
                change(row.key, { name: text });
              }}
            />
            <TemplateField
              label={<span className="sr-only">{t`Value of ${name}`}</span>}
              field={`values[${String(index)}].value`}
              findings={findings}
              mono={false}
              howTo={false}
              value={row.value}
              onChange={(text) => {
                change(row.key, { value: text });
              }}
            />
            {locked ? null : (
              <AriaButton
                aria-label={t`Remove ${name}`}
                className={cx(iconKey, "mt-0.75")}
                onPress={() => {
                  onChange(rows.filter((other) => other.key !== row.key));
                }}
              >
                <IconX size={14} stroke={2} />
              </AriaButton>
            )}
          </div>
        );
      })}
      {own.problems.map((message) => (
        <p key={message} className="type-small text-fail-text">
          {message}
        </p>
      ))}
      {locked ? null : (
        <AriaButton
          className="flex cursor-pointer items-center gap-1.5 self-start rounded-key px-1 py-1 type-label text-ink key-motion outline-none hover:bg-hover pressed:bg-key-quiet-pressed focus-visible:outline-2 focus-visible:outline-focus"
          onPress={() => {
            onChange([...rows, { key: crypto.randomUUID(), name: "", value: "" }]);
          }}
        >
          <IconPlus aria-hidden="true" size={14} stroke={2} />
          <Trans>Add a value</Trans>
        </AriaButton>
      )}
    </div>
  );
}
