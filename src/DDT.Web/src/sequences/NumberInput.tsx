// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useState } from "react";

import type { ControlProps } from "./FormField";

import styles from "./form.module.scss";

export interface NumberInputProps {
  control: ControlProps;
  value: number;
  onChange: (value: number) => void;
  min?: number;
}

// Only whole numbers reach the document. While the field is being typed in it shows what was typed, even when
// that is no number yet, such as an empty field.
export function NumberInput({ control, value, onChange, min }: NumberInputProps) {
  const [text, setText] = useState<string | null>(null);

  return (
    <input
      {...control}
      className={styles.number}
      type="number"
      inputMode="numeric"
      step={1}
      min={min}
      value={text ?? String(value)}
      onChange={(event) => {
        const typed = event.target.value;
        const number = Number(typed);

        setText(typed);

        if (typed.trim() !== "" && Number.isSafeInteger(number)) {
          onChange(number);
        }
      }}
      onBlur={() => {
        setText(null);
      }}
    />
  );
}
