// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { useContext, useState } from "react";

import { TextField } from "@/ui/TextField";

import { EditorLock } from "../editorLock";
import { parseCodes } from "../steps";
import type { FieldBase } from "./fieldBase";
import { findingProps } from "./findingProps";

// Exit codes as a list such as "0, 3010". What was typed stays while the field is typed in, even when it is no list
// yet; the step keeps the last list that was one.
export function CodesSetting({
  label,
  field,
  findings,
  hint,
  className,
  value,
  onChange,
}: FieldBase & { value: readonly number[]; onChange: (value: number[]) => void }) {
  const locked = useContext(EditorLock);
  const [text, setText] = useState<string | null>(null);
  const invalid = text !== null && parseCodes(text) === null;
  const findingsOf = findingProps(findings, field, hint);

  return (
    <div data-field={field} className={className}>
      <TextField
        label={label}
        value={text ?? value.join(", ")}
        isReadOnly={locked}
        mono
        inputMode="numeric"
        autoComplete="off"
        spellCheck="false"
        onChange={(typed) => {
          const codes = parseCodes(typed);

          setText(typed);

          if (codes !== null) {
            onChange(codes);
          }
        }}
        onBlur={() => {
          setText(null);
        }}
        hint={findingsOf.hint}
        isInvalid={invalid || findingsOf.isInvalid}
        errorMessage={
          invalid
            ? t`Enter whole numbers from -2147483648 to 2147483647, separated by commas. Until then the last list stays.`
            : findingsOf.errorMessage
        }
      />
    </div>
  );
}
