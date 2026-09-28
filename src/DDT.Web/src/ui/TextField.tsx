// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { ReactNode } from "react";
import {
  FieldError,
  Input,
  Label,
  Text,
  TextArea,
  TextField as AriaTextField,
  type TextFieldProps as AriaTextFieldProps,
} from "react-aria-components";

import { cx } from "./cx";

// The frame changes colour in fast, as when a value turns invalid, but the focus ring appears at once.
export const fieldClass =
  "w-full rounded-key bg-field px-2.5 text-ink shadow-[inset_0_0_0_1px_var(--color-control)] motion-colors outline-none " +
  "focus:shadow-[inset_0_0_0_2px_var(--color-focus)] focus:duration-0 invalid:shadow-[inset_0_0_0_1.5px_var(--color-fail-text)] " +
  "disabled:cursor-not-allowed disabled:opacity-60";

export interface TextFieldProps extends Omit<AriaTextFieldProps, "className" | "children"> {
  label: ReactNode;
  hint?: ReactNode;
  errorMessage?: ReactNode;
  placeholder?: string;
  // Identifiers such as MAC addresses and computer names read better in the mono face.
  mono?: boolean;
  multiline?: boolean;
  rows?: number;
  className?: string;
}

export function TextField({
  label,
  hint,
  errorMessage,
  placeholder,
  mono = false,
  multiline = false,
  rows = 4,
  className,
  ...props
}: TextFieldProps) {
  const input = cx(fieldClass, mono ? "type-data" : "type-body", multiline ? "py-2" : "h-9.5");
  const hintText = placeholder === undefined ? {} : { placeholder };

  return (
    <AriaTextField {...props} className={cx("flex flex-col gap-1.5", className)}>
      <Label className="type-label text-ink">{label}</Label>
      {multiline ? (
        <TextArea {...hintText} rows={rows} className={cx(input, "resize-y")} />
      ) : (
        <Input {...hintText} className={input} />
      )}
      {hint ? (
        <Text slot="description" className="type-small text-muted">
          {hint}
        </Text>
      ) : null}
      <FieldError className="type-small text-fail-text">{errorMessage}</FieldError>
    </AriaTextField>
  );
}
