// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import { useContext, useId, useState } from "react";

import { PickedRow } from "@/applications/drawer/PickedRow";
import { Button } from "@/ui/Button";
import { FieldErrorText } from "@/ui/FieldErrorText";
import { TextField } from "@/ui/TextField";

import { EditorLock } from "../editorLock";
import { fieldFindings, type Findings } from "../problems";

const field = "firstLogonCommands";

export const MAX_FIRST_LOGON_COMMANDS = 20;

// The commands Windows runs in order at the first sign-in of an administrator.
export function FirstLogonCommandsSetting({
  findings,
  value,
  onChange,
}: {
  findings: Findings;
  value: readonly string[];
  onChange: (value: string[]) => void;
}) {
  const { t } = useLingui();
  const locked = useContext(EditorLock);
  const labelId = useId();
  const [typed, setTyped] = useState("");
  const full = value.length >= MAX_FIRST_LOGON_COMMANDS;

  const add = () => {
    const command = typed.trim();

    if (command !== "" && !full) {
      onChange([...value, command]);
      setTyped("");
    }
  };

  const move = (from: number, to: number) => {
    const next = [...value];
    const [moved] = next.splice(from, 1);

    if (moved !== undefined) {
      next.splice(to, 0, moved);
      onChange(next);
    }
  };

  return (
    <div
      role="group"
      aria-labelledby={labelId}
      data-field={field}
      className="flex flex-col gap-2 sm:col-span-2"
    >
      <span id={labelId} className="type-label text-ink">
        <Trans>Commands at the first sign-in</Trans>
      </span>
      {value.length > 0 ? (
        <ol className="flex flex-col gap-1 type-data">
          {value.map((command, index) => (
            <PickedRow
              key={`${String(index)}:${command}`}
              id={String(index)}
              index={index}
              name={command}
              note={null}
              extra={undefined}
              isLast={index === value.length - 1}
              isReadOnly={locked}
              errors={fieldFindings(findings, `${field}[${String(index)}]`).problems}
              onMove={(to) => {
                move(index, to);
              }}
              onRemove={() => {
                onChange(value.filter((_, at) => at !== index));
              }}
            />
          ))}
        </ol>
      ) : null}
      <FieldErrorText errors={fieldFindings(findings, field).problems} />
      {locked ? null : (
        <div className="flex items-start gap-2">
          <TextField
            label={<span className="sr-only">{t`Add a command`}</span>}
            placeholder={full ? t`At most 20 commands` : t`Add a command`}
            className="flex-1"
            mono
            autoComplete="off"
            spellCheck="false"
            isDisabled={full}
            value={typed}
            onChange={setTyped}
            onKeyDown={(event) => {
              if (event.key === "Enter") {
                add();
              }
            }}
          />
          <Button isDisabled={full || typed.trim() === ""} onPress={add}>
            <Trans>Add</Trans>
          </Button>
        </div>
      )}
    </div>
  );
}
