// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useContext } from "react";

import { ListBoxItem, Select } from "@/ui/Select";
import { TextField } from "@/ui/TextField";

import { EditorLock } from "../editorLock";
import type { Choice, FieldBase } from "./fieldBase";
import { findingProps } from "./findingProps";

// When read-only, the choice shows as text, because a disabled list is hard to read.
export function ChoiceSetting({
  label,
  field,
  findings,
  hint,
  className,
  value,
  choices,
  onChange,
  placeholder,
}: FieldBase & {
  // Null shows the placeholder.
  value: string | null;
  choices: Choice[];
  onChange: (value: string) => void;
  placeholder?: string;
}) {
  const locked = useContext(EditorLock);
  const findingsOf = findingProps(findings, field, hint);

  if (locked) {
    const chosen = choices.find((choice) => choice.id === value);

    return (
      <div data-field={field} className={className}>
        <TextField
          label={label}
          value={chosen?.label ?? placeholder ?? ""}
          isReadOnly
          {...findingsOf}
        />
      </div>
    );
  }

  return (
    <div data-field={field} className={className}>
      <Select
        label={label}
        value={value}
        onChange={(key) => {
          if (key !== null) {
            onChange(String(key));
          }
        }}
        {...(placeholder === undefined ? {} : { placeholder })}
        {...findingsOf}
      >
        {choices.map((choice) => (
          <ListBoxItem
            key={choice.id}
            id={choice.id}
            textValue={choice.label}
            isDisabled={choice.isDisabled === true}
            {...(choice.description === undefined ? {} : { description: choice.description })}
          >
            {choice.label}
          </ListBoxItem>
        ))}
      </Select>
    </div>
  );
}
