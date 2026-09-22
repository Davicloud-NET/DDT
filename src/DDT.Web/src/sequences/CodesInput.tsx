// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useState } from "react";

import type { ControlProps } from "./FormField";
import { parseCodes } from "./steps";

import styles from "./form.module.scss";

export interface CodesInputProps {
  control: ControlProps;
  value: readonly number[];
  onChange: (value: number[]) => void;
}

// Exit codes as a list such as "0, 3010". What was typed stays while the field is being typed in.
export function CodesInput({ control, value, onChange }: CodesInputProps) {
  const [text, setText] = useState<string | null>(null);
  const invalid = text !== null && parseCodes(text) === null;

  return (
    <>
      <input
        {...control}
        className={styles.codes}
        type="text"
        inputMode="numeric"
        value={text ?? value.join(", ")}
        onChange={(event) => {
          const typed = event.target.value;
          const codes = parseCodes(typed);

          setText(typed);

          if (codes !== null) {
            onChange(codes);
          }
        }}
        onBlur={() => {
          setText(null);
        }}
      />
      {invalid && (
        <span className={styles.local} role="alert">
          Enter whole numbers separated by commas. Until then the last list stays.
        </span>
      )}
    </>
  );
}
