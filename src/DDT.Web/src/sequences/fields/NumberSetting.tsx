// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useContext } from "react";

import { NumberField } from "@/ui/NumberField";

import { EditorLock } from "../editorLock";
import { isInt32 } from "../steps";
import type { FieldBase } from "./fieldBase";
import { findingProps } from "./findingProps";

// A whole number the server can store. The keys step it, and a number past the bounds is brought within them when
// the field is left.
export function NumberSetting({
  label,
  field,
  findings,
  hint,
  className,
  value,
  onChange,
  minValue,
  maxValue = 2_147_483_647,
}: FieldBase & {
  value: number;
  onChange: (value: number) => void;
  minValue: number;
  maxValue?: number;
}) {
  const locked = useContext(EditorLock);

  return (
    <div data-field={field} className={className}>
      <NumberField
        label={label}
        value={value}
        minValue={minValue}
        maxValue={maxValue}
        step={1}
        formatOptions={{ useGrouping: false, maximumFractionDigits: 0 }}
        isReadOnly={locked}
        onChange={(number) => {
          if (isInt32(number)) {
            onChange(number);
          }
        }}
        {...findingProps(findings, field, hint)}
      />
    </div>
  );
}
