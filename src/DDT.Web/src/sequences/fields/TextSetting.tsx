// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useContext } from "react";

import { TextField } from "@/ui/TextField";

import { EditorLock } from "../editorLock";
import type { FieldBase } from "./fieldBase";
import { findingProps } from "./findingProps";

export function TextSetting({
  label,
  field,
  findings,
  hint,
  className,
  value,
  onChange,
  placeholder,
  mono = false,
  multiline = false,
  rows,
  maxLength,
}: FieldBase & {
  value: string;
  onChange: (value: string) => void;
  placeholder?: string;
  mono?: boolean;
  multiline?: boolean;
  rows?: number;
  maxLength?: number;
}) {
  const locked = useContext(EditorLock);

  return (
    <div data-field={field} className={className}>
      <TextField
        label={label}
        value={value}
        onChange={onChange}
        isReadOnly={locked}
        mono={mono}
        multiline={multiline}
        spellCheck={mono ? "false" : "true"}
        autoComplete="off"
        {...(rows === undefined ? {} : { rows })}
        {...(placeholder === undefined || locked ? {} : { placeholder })}
        {...(maxLength === undefined ? {} : { maxLength })}
        {...findingProps(findings, field, hint)}
      />
    </div>
  );
}
